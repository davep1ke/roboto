using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace RobotoChatBot.Modules
{

    public enum xyzzy_Statuses { Stopped, useDefaults, SetGameLength, setPackFilter, setMinHours, setMaxHours, cardCastImport, Invites, Question, Judging, waitingForNextHand }

    
    /// <summary>
    /// CHAT (i.e. game) Data to be stored in the XML store
    /// </summary>
    [XmlType("mod_xyzzy_data")]
    [Serializable]
    public class mod_xyzzy_chatdata : RobotoModuleChatDataTemplate
    {
        private const string emoji_check = "\u2705";
        private const string emoji_cross = "\u274C";

        public int maxPacksPerPage = 30;

        //core chat data
        public List<mod_xyzzy_player> players = new List<mod_xyzzy_player>();
        public int lastPlayerAsked = -1; //This is the position in the array, so int is fine! todo - should be an ID!
        public xyzzy_Statuses status = xyzzy_Statuses.Stopped;
        public DateTime statusChangedTime = DateTime.Now;
        public DateTime statusCheckedTime = DateTime.Now;
        public DateTime statusMiniCheckedTime = DateTime.Now;
        public DateTime lastHandStartedTime = DateTime.Now;

        public bool remindersSent = false;

        //chat settings
        [System.Obsolete("usepackFilterIDs")]
        public List<String> packFilter = new List<string> { };// { "Cards Against Humanity" };

        // Starts empty rather than defaulting to a specific pack here - this field initializer runs
        // inside chat.initPlugins() (Storage/chat.cs), which fires for every chat reconstruction
        // during settings.load()'s bulk reload at startup, before Roboto.Settings is assigned, so it
        // can't safely consult the pack catalog to pick a real default. Resolved lazily instead, the
        // first time it's actually needed - see ensureDefaultPackFilter().
        public List<Guid> packFilterIDs = new List<Guid>();

        // Must be a real persisted field (not transient/[JsonIgnore]) - a player deliberately
        // emptying packFilterIDs via "None" needs to stay empty across restarts too, not just within
        // the current process, or ensureDefaultPackFilter() would silently refill it the first time
        // anything calls packEnabled() after the next reload.
        public bool packFilterResolved = false;

        public int enteredQuestionCount = 10;
        public int maxWaitTimeHours = 0;
        public int minWaitTimeHours = 0;


        //Store these here, per-chat, so that theres no overlap between chats. Could also help if we want to filter card sets later. Bit heavy on memory, probably. 
        public List<String> remainingQuestions = new List<string>();
        public string currentQuestion;
        public List<String> remainingAnswers = new List<string>();
        
        //handled by super
        //internal mod_xyzzy_chatdata() { }

        public void setStatus (xyzzy_Statuses newStatus)
        {
            status = newStatus;
            remindersSent = false;

            //update some date pointers for future reference
            statusChangedTime = DateTime.Now;
            if (newStatus == xyzzy_Statuses.Question) { lastHandStartedTime = DateTime.Now; }
            log("Status for " + chatID + " now " + newStatus.ToString(), logging.loglevel.verbose);

        }

        public override bool isPurgable()
        {
            if (statusChangedTime > DateTime.Now.AddDays(Roboto.Settings.killInactiveChatsAfterXDays * -1 )) { return false; }
            return true;
        }

        /// <summary>
        /// Completely stop the game and clear any player data
        /// </summary>
        public void reset()
        {
            setStatus(xyzzy_Statuses.Stopped);
            players.Clear();
            remainingAnswers.Clear();
            remainingQuestions.Clear();
            lastPlayerAsked = -1;
            Messaging.clearExpectedReplies(chatID, typeof(mod_xyzzy));
        }

        /*/when the status is changed, make a note of the date.
        private void statusChanged()
        {
            statusChangedTime = DateTime.Now;
        }*/

        public mod_xyzzy_player getPlayer(long playerID)
        {
            foreach (mod_xyzzy_player existing in players)
            {
                if (existing.playerID == playerID) { return existing; }
            }
            return null;
        }

        internal mod_xyzzy_player getPlayer(string p)
        {
            foreach (mod_xyzzy_player existing in players)
            {
                if (existing.name.Trim() == p) { return existing; }
            }
            return null;
        }



        public mod_xyzzy_coredata getLocalData()
        {
            return Plugins.getPluginData<mod_xyzzy_coredata>();
        }

        internal bool addPlayer(mod_xyzzy_player newPlayer)
        {

            mod_xyzzy_player existing = getPlayer(newPlayer.playerID);
            if (existing == null)
            {
                players.Add(newPlayer);
                return true;
            }

            return false;
        }

        /// <summary>Silly robot-themed names for "Add Bots" players - not legacy wording, legacy
        /// never had bot players at all. Ported from the abandoned rewrite branch's own list (see
        /// MIGRATION.md phase 9).</summary>
        private static readonly string[] robotNames = new string[]
        {
            "Clanky", "Rustbucket", "Botzilla", "Sir Beeps-a-Lot", "Cogsworth", "Tin Can Tony",
            "Byte Me", "Circuit Breaker", "Metal Mickey", "Robzilla", "Wheatley Jr.", "Servo",
            "Clank", "Bit Bot", "Automaton Prime", "Robo Ricky", "Marvin the Paranoid Droid",
            "Botimus Prime", "Chrome Dome", "Rusty Gears", "The Great Cogsby", "Robbie Roboto",
            "Deep Fryer 3000", "Anti-Skynet", "Grumpy Gearbox", "Roborto", "Beep Boop Bandit",
            "Captain Cogwheel", "Sparky McSparkface", "The Malfunctioner",
        };

        /// <summary>Picks a name not already in use by another bot in this game, so two bots never
        /// look identical in a round announcement - falls back to a numbered variant only once the
        /// whole list is exhausted.</summary>
        private string nextRobotName()
        {
            List<string> used = players.Where(p => p.isBot).Select(p => p.name).ToList();
            List<string> available = robotNames.Where(n => !used.Contains(n)).ToList();
            if (available.Count > 0) { return available[settings.getRandom(available.Count)]; }
            return "Robot " + (used.Count + 1);
        }

        /// <summary>Adds `count` bot players with robot-themed names - "Add Bots" (mod_xyzzy.cs),
        /// a carried-forward delta from the abandoned rewrite branch (see MIGRATION.md phase 9);
        /// legacy never had non-human players at all. Bot playerIDs are synthetic negative values
        /// (real Telegram user IDs are always positive) so they can never collide with a real
        /// player and are an unambiguous "never try to DM this" signal in askQuestion/beginJudging.
        /// Returns the names added so callers can echo them back without re-deriving which players
        /// are new.</summary>
        internal List<string> addBots(int count)
        {
            long nextBotID = players.Where(p => p.isBot).Select(p => p.playerID).DefaultIfEmpty(0).Min() - 1;
            List<string> added = new List<string>();
            for (int i = 0; i < count; i++)
            {
                string botName = nextRobotName();
                players.Add(new mod_xyzzy_player(botName, nextBotID, true));
                added.Add(botName);
                nextBotID--;
            }
            return added;
        }

        /// <summary>"Remove All Bots" - companion to addBots, offered on the same keyboard
        /// (askAddBotsCount) in both the Invites setup flow and /xyzzy_settings. Mid-game, bots are
        /// removed one at a time via the existing removePlayer so judge-reassignment/mid-round
        /// bookkeeping (and, if it's genuinely warranted, the "not enough players, ending game"
        /// wrap-up) stay exactly as they already are for any other player removal. Still in Invites
        /// (setup, no round dealt yet) that same wrap-up would fire as soon as bot removal dropped
        /// the roster to 2 - reading "not enough players" and ending a game that was never actually
        /// started - so pre-game this drops the bot entries straight from the roster instead, with no
        /// round/judge state to preserve anyway.</summary>
        internal List<string> removeAllBots()
        {
            List<mod_xyzzy_player> bots = players.Where(p => p.isBot).ToList();
            List<string> removed = bots.Select(b => b.name).ToList();

            if (status == xyzzy_Statuses.Invites)
            {
                players.RemoveAll(p => p.isBot);
            }
            else
            {
                foreach (mod_xyzzy_player bot in bots) { removePlayer(bot.playerID); }
            }
            return removed;
        }

        internal bool removePlayer(long playerID)
        {
            log("Removing " + playerID.ToString() + ". Currently " + players.Count + " players. Judge ID is pos " + lastPlayerAsked);

            mod_xyzzy_player existing = getPlayer(playerID);
            //keep track of the judge!
            mod_xyzzy_player judge = null;
            if (lastPlayerAsked >= 0 && lastPlayerAsked < players.Count)
            {
                judge = players[lastPlayerAsked];
            }
            
            if (players.Count == 0)
            {
                log("No players in game", logging.loglevel.high);
                Messaging.SendMessage(chatID, "No players in the game to kick");
                return false;
            }
            else if (existing == null)
            {
                //check everything OK
                log("Couldnt find player to remove, checking consistency", logging.loglevel.warn);
                Messaging.SendMessage(chatID, "Couldn't find player to kick");
                check();
                return false;
            }
            else if (players.Count <= 2 && existing != null)
            {
                //removing last player
                Messaging.SendMessage(chatID, "Not enough players to continue, ending game.");
                wrapUp();
                players.Remove(existing);
                return true;
            }
            else if (judge == null )
            {
                log("Couldn't find judge! ID was" + lastPlayerAsked + ", resetting to 0", logging.loglevel.high);
                lastPlayerAsked = 0;
                judge = players[0];
                //this should really be unneccessary - but had some issues so check anyway
                if (judge == null)
                {
                    log("Soemthing went really wrong and couldnt set player0 to judge", logging.loglevel.critical);
                    return false;
                }
            }

            else
            {
                log("Removing " + playerID + ". Current judge is " + judge.playerID + " at pos " + lastPlayerAsked, logging.loglevel.verbose);
            }

            //logging
            string logtxt = "Current State: " + status.ToString() + " Existing player: ";
            if (existing == null) { logtxt += " is null "; } else { logtxt += existing.ToString() + " / " +existing.playerID; }
            logtxt += ". Judge ";
            if (judge == null) { logtxt += " is null "; } else { logtxt += judge.ToString() + " / " + judge.playerID; }
            log(logtxt, logging.loglevel.verbose);
            
            
            if (existing != null)
            {
                players.Remove(existing);
                Messaging.SendMessage(chatID, "Removed " + existing.ToString());

                //reset the judge ID. Judge should really be populated by this point
                if (judge != null && existing == judge)
                {
                    //did we just remove the judge? sort out the judge ID and send a message confirming what happened
                    if (lastPlayerAsked >= players.Count)
                    {
                        lastPlayerAsked = 0;
                    }
                    judge = players[lastPlayerAsked];
                    if (judge != null)
                    {
                        judge.selectedCards.Clear();
                        Messaging.SendMessage(chatID, "Judge " + existing.ToString() + " has left, judge is now " + judge.ToString());
                        log("Judge " + existing.ToString() + " has left, judge is now " + judge.ToString(), logging.loglevel.verbose);
                        //if we were in the middle of judging, resend the judge message.
                        if (status == xyzzy_Statuses.Judging)
                        {
                            log("Resending judges message as player removed mid-judge", logging.loglevel.verbose);
                            beginJudging(true);
                        }

                    }
                    else
                    {
                        log("Something went wrong removing the judge", logging.loglevel.high);
                    }
                }
                else if  (judge != null)
                { 
                    for (int i = 0; i < players.Count; i++)
                    {
                        if (players[i] == judge)
                        {
                            lastPlayerAsked = i;
                            log("lastplayer ID reset to  " + i, logging.loglevel.verbose);
                        }
                    }
                }
                else
                {
                    log("Soemthing went really wrong and couldnt find judge to reset", logging.loglevel.critical);
                }
                //clear any expected replies for the player we removed
                //TODO - move to Messaging class.
                List<ExpectedReply> matchedReplies = Messaging.getExpectedReplies(typeof(mod_xyzzy), chatID, existing.playerID);
                foreach (ExpectedReply exr in matchedReplies)
                {
                    // Messaging.removeReply, not a direct list .Remove() - expectedReplies is shared
                    // with the phase-4 background scheduler thread, see ChatKeyedLock's own comment.
                    Messaging.removeReply(exr);
                    log("Removed expectedReply: " + exr.messageData);
                }

                // Real live bug: found by leaving a solo-plus-bots game during setup (Invites) -
                // removePlayer had no equivalent of askQuestion()'s own "everyone left" safety stop,
                // so the last human leaving during setup silently handed "judge" to a bot and left
                // the game stuck in Invites forever (no human left who could ever tap Start, or
                // reply to anything - bots can't). askQuestion() only catches this once a round is
                // actually dealt, which setup-phase removals never reach at all.
                if (players.Count > 0 && players.All(p => p.isBot))
                {
                    Messaging.SendMessage(chatID, "Everyone left - stopping the game.");
                    wrapUp();
                    return true;
                }

                //cant hurt at this stage...
                check();
                return true;
            }
            else
            {
                //check everything OK
                log("Couldnt remove - something wierd happened", logging.loglevel.high);
                check();
                return false;
             }
        }

        internal void askQuestion(bool force)
        {
            // Safety stop: if every remaining player is a bot (e.g. every real player has left via
            // /xyzzy_leave), bots answering and judging automatically below would otherwise chain
            // through rounds forever with nobody watching. Ported from the abandoned rewrite
            // branch's own "all-bots-left safety stop" for "Add Bots" (MIGRATION.md phase 9),
            // confirmed load-bearing there via a real StackOverflowException, not just a failed
            // assertion.
            if (players.Count > 0 && players.All(p => p.isBot))
            {
                Messaging.SendMessage(chatID, "Everyone left - stopping the game.");
                wrapUp();
                return;
            }

            Roboto.Settings.stats.logStat(new statItem("Hands Played", typeof(mod_xyzzy)));
            mod_xyzzy_coredata localData = getLocalData();
            // Legacy's own TODO here ("this causes issues if someone is changing settings in the
            // middle of a round") called this exact bug out, unfixed since. Confirmed live: a
            // player's /xyzzy_settings request queues an ExpectedReply behind their still-
            // outstanding "Question" one (Messaging's per-user single-outstanding-message
            // serialization) - once they finish answering and a new round deals immediately (a bot
            // judge auto-decides with no human wait, or any other same-tick round advance), this
            // call used to blanket-clear *every* mod_xyzzy ExpectedReply for the chat regardless of
            // messageData, wiping the still-queued, not-yet-sent Settings menu before it ever got a
            // chance to send. Scoped to "Question" only - the actual stale state this call exists
            // to clean up (a previous round's unanswered players) - so unrelated queued
            // conversations for other messageData contexts survive into the new round instead.
            Messaging.clearExpectedReplies(chatID, typeof(mod_xyzzy), "Question");

            
            //check that the question card still exists. Remove any dead cards
            mod_xyzzy_card question = null;
            while (question == null && remainingQuestions.Count > 0)
            {
                question = localData.getQuestionCard(remainingQuestions[0]);
                if (question == null)
                {
                    log("Tried to ask q " + remainingQuestions[0] + " but has been removed from the cache.", logging.loglevel.high);
                    remainingQuestions.RemoveAt(0);
                }
            }

            //are we out of qcards, and need to refill?
            if (question == null)
            {
                log("Out of Question Cards, refilling.", logging.loglevel.high);
                Messaging.SendMessage(chatID, "All questions have been used up, pack has been refilled!");
                addQuestions();

                // Real crash hit live: an empty/fully-filtered-out card pool leaves
                // remainingQuestions still empty after the refill attempt above - indexing [0]
                // unconditionally threw ArgumentOutOfRangeException the moment a game tried to
                // start. Send a clear message and bail instead of dealing a hand that can't happen.
                if (remainingQuestions.Count == 0)
                {
                    log("Still no question cards available after refilling - no enabled pack has any cards.", logging.loglevel.critical);
                    Messaging.SendMessage(chatID, "No cards are available to deal - check your pack settings with /xyzzy_settings.");
                    setStatus(xyzzy_Statuses.waitingForNextHand);
                    return;
                }
                question = localData.getQuestionCard(remainingQuestions[0]);
            }


            //carry on if force is ticked (e.g. commands from chat for /extend and /question)
            //are we in a quiet period? 
            if (! force && mod_standard.isTimeInQuietPeriod(chatID, DateTime.Now))
            {
                log("About to ask question, but we are in a quiet period. Waiting until awake.", logging.loglevel.warn);
                setStatus(xyzzy_Statuses.waitingForNextHand);

            }
            //do we have a throttle set? If so, are we within the window? 
            else if (!force && minWaitTimeHours > 0 && lastHandStartedTime.Add(new TimeSpan(minWaitTimeHours, 0, 0)) > DateTime.Now)
            {
                log("About to ask question, but we are throttling.", logging.loglevel.warn);
                setStatus(xyzzy_Statuses.waitingForNextHand);
            }
            else
            {
                

                //good to ask the question - find out if there are any left
                if (remainingQuestions.Count > 0 && question != null)
                {
                    int playerPos = lastPlayerAsked + 1;
                    if (playerPos >= players.Count) { playerPos = 0; }
                    mod_xyzzy_player tzar = players[playerPos];

                    //Keep a list of players who we can't send to. Kick them after trying all the messages
                    List<mod_xyzzy_player> dormantPlayers = new List<mod_xyzzy_player>();

                    //loop through each player and act accordingly
                    foreach (mod_xyzzy_player player in players)
                    {
                        //throw away old cards and select new ones.
                        player.selectedCards.Clear();
                        player.topUpCards(10, remainingAnswers, chatID);
                        long messageID = long.MaxValue;
                        if (player.isBot)
                        {
                            //"Add Bots" player - never DMed, see the auto-answer loop below.
                        }
                        else if (player == tzar)
                        {
                            messageID =  Messaging.SendMessage(player.playerID, "Its your question! You ask:" + "\r\n" + question.text, player.name,  false, -1, true);
                        }
                        else
                        {
                            /*int questionMsg = TelegramAPI.GetReply(player.playerID,, -1, true, player.getAnswerKeyboard(localData));*/
                            string questionText = tzar.name + " asks: " + "\r\n" + question.text;
                            //we are expecting a reply to this:
                            messageID = Messaging.SendQuestion(chatID, player.playerID, questionText, true, typeof(mod_xyzzy), "Question", null, -1, true, player.getAnswerKeyboard(localData));
                        }

                        if (messageID == -403) //bot doesnt have access to send message. Probably blocked
                        {
                            dormantPlayers.Add(player);
                        }
                    }

                    lastPlayerAsked = playerPos;
                    currentQuestion = remainingQuestions[0];
                    int count = remainingQuestions.Count; 
                    remainingQuestions.Remove(currentQuestion);

                    log("Removing " + question.uniqueID + " from remainingQuestions. Went from " + count.ToString() + " to " + remainingQuestions.Count.ToString() + " remaining.", logging.loglevel.verbose);

                    //remove anyone who should be dormant
                    foreach(mod_xyzzy_player p in dormantPlayers)
                    {
                        removePlayer(p.playerID);
                    }


                    setStatus(xyzzy_Statuses.Question);

                    // Bots never receive a DM (see the loop above) - they answer immediately with
                    // a random card from their dealt hand instead, looping until they've submitted
                    // the question's full nrAnswers, same as a real player would across several
                    // taps. "Add Bots" (MIGRATION.md phase 9) - legacy never had non-human players
                    // at all. Re-checks status each iteration: an earlier bot's answer may already
                    // have completed the round (logAnswer calls beginJudging once everyone's in),
                    // moving status off Question entirely - including, if the new judge is also a
                    // bot, all the way through to a fresh Question via judgesResponse's own
                    // askQuestion(false) call.
                    foreach (mod_xyzzy_player botPlayer in players.Where(p => p != tzar && p.isBot).ToList())
                    {
                        while (status == xyzzy_Statuses.Question
                            && botPlayer.selectedCards.Count < question.nrAnswers
                            && botPlayer.cardsInHand.Count > 0)
                        {
                            string cardID = botPlayer.cardsInHand[settings.getRandom(botPlayer.cardsInHand.Count)];
                            mod_xyzzy_card card = localData.getAnswerCard(cardID);
                            logAnswer(botPlayer.playerID, card.text);
                        }
                    }
                }
                else
                {
                    //no questions left, finish game
                    log("No more questions, ending.");
                    wrapUp();
                }
            }
        }

        public bool logAnswer(long playerID, string answer)
        {
            mod_xyzzy_coredata localData = getLocalData();
            mod_xyzzy_player player = getPlayer(playerID);
            mod_xyzzy_card question = localData.getQuestionCard(currentQuestion);
            //make sure the response is in the list of cards the player has
            mod_xyzzy_card answerCard = null;

            string possibleAnswers = "";
            foreach (string cardUID in player.cardsInHand)
            {
                //find the card, then check the text matches the response.
                mod_xyzzy_card card = localData.getAnswerCard(cardUID);
                //cleanse both bits of text, limit to 100 chars as keybaord cuts off sometimes. Try exact match first jsut in case the cleanse does something wierd. 
                if (card != null && 
                    (card.text == answer
                    || (answer.Length > 100 ? answer.Substring(0, 100) : answer) == (card.text.Length > 100 ? card.text.Substring(0, 100) : card.text)
                    || Helpers.common.cleanseText(card.text, 100) == Helpers.common.cleanseText(answer, 100))
                    )
                {
                    answerCard = card;
                }
                possibleAnswers += card.text +", ";
            }

            if (answerCard == null)
            {
                Roboto.Settings.stats.logStat(new statItem("Bad Responses", typeof(mod_xyzzy)));
                log("Couldn't match card against '" + answer 
                    + "' in chat " + chatID
                    + " which has question " + currentQuestion + " (" 
                    + (question.text.Length > 10 ? question.text.Substring(0,10) : question.text )
                    + ") out - probably an invalid response", logging.loglevel.warn );
                log("Possible answers for player " + playerID + " were: " + possibleAnswers, logging.loglevel.verbose);
                //couldnt find answer, reask
                string questionText = players[lastPlayerAsked].name + " asks: " + "\r\n" + question.text;
                //we are expecting a reply to this:
                Messaging.SendQuestion(chatID, player.playerID, "Not a valid answer! Try again. " + questionText, true, typeof(mod_xyzzy), "Question", null,  -1, true, player.getAnswerKeyboard(localData));

                //Messaging.SendMessage(playerID, "Not a valid answer! Reply to the original message again, using the keyboard");
                return false;
            }
            else
            {
                //valid response, remove the card from the deck, and add it to our list of responses
                bool success = player.SelectAnswerCard(answerCard.uniqueID);
                if (!success)
                {
                    log("Card " + answerCard.uniqueID + " from " + player.name + " couldnt be selected for some reason!", logging.loglevel.high);
                    throw new ArgumentOutOfRangeException ("Card couldnt be selected for some reason!");
                }
                else
                {
                    //just check if this needs more responses:
                    // !player.isBot guard: a bot mid-multi-answer-question never gets DMed here
                    // either, matching askQuestion's own dealing loop - the bot auto-answer while
                    // loop (askQuestion) keeps calling logAnswer for the same bot until it's
                    // selected enough cards, so no prompt is needed. Found alongside the
                    // allPlayersAnswered() fix above (same root cause: bot players don't have a
                    // real Telegram chat to message) while investigating a live-bot report - see
                    // MIGRATION.md. Latent until now since it only bites multi-answer questions.
                    if (!player.isBot && player.selectedCards.Count != question.nrAnswers)
                    {
                        Messaging.SendQuestion(chatID, player.playerID, "Pick your next card", true, typeof(mod_xyzzy), "Question", null, -1, true, player.getAnswerKeyboard(localData),false,false,true);
                    }
                }
            }

            //are we ready to start judging?
            // allPlayersAnswered() checks each non-judge player's real selectedCards count, not
            // outstandingResponses()'s ExpectedReply-based tracking - bot players ("Add Bots",
            // MIGRATION.md phase 9) never get a DM/ExpectedReply at all, so outstandingResponses()
            // structurally can't see them. In a game with no other real players left to answer, that
            // meant judging fired the instant the *first* bot answered - outstandingResponses()
            // read zero (no humans outstanding) while later bots in the same auto-answer loop
            // hadn't gone yet, showing them as having "skipped" in the judging message. Found via a
            // live round-trip against the beefy test bot (solo player + 2 bots) - see MIGRATION.md.
            if (allPlayersAnswered())
            {
                log("All answers received, judging", logging.loglevel.verbose);
                beginJudging();
            }
            else
            {
                string logtxt = "Still waiting for answers. Status is:";
                foreach(mod_xyzzy_player p in players )
                {
                    logtxt += "\r\n" + p.name + " " + p.playerID + " - " + p.selectedCards.Count() + " / " + question.nrAnswers ;
                    foreach (ExpectedReply e in Messaging.getExpectedReplies(typeof(mod_xyzzy), chatID, p.playerID))
                    {
                        logtxt += " ExpectedReply: " + e.messageData + " sent= " + e.isSent().ToString();
                    }
                }
                log(logtxt, logging.loglevel.verbose);
            }


            return true;
        }

        /// <summary>
        /// Sends a settings page to a user. 
        /// </summary>
        /// <param name="m"></param>
        public void sendSettingsMessage(message m)
        {
            bool isAdmin = getChat().isChatAdmin(m.userID);

            string message = "This allows you to change the game settings for the xyzzy game. Current values are listed below.";
            List<string> keyboardOptions = new List<string>();
            if (status == xyzzy_Statuses.Stopped)
            {
                message += " No game is running. You should probably *Cancel* this, then start a new game by typing /xyzzy\\_start in your group chat.";
                if (players.Count > 1) { message += " You can also continue from the last game by selecting *Extend* ."; }

            }
            keyboardOptions.Add("Cancel");
            //check for admin

            if (isAdmin)
            {

                //pack filter
                message += "\r\n- " + packFilterIDs.Count + " packs currently enabled. You can view, enable and disable with *Change Packs* ";
                keyboardOptions.Add("Change Packs");
                //length
                message += "\r\n- " + "Game will " + (this.enteredQuestionCount == -1? "not end." :( "last " + this.enteredQuestionCount + " questions.")) + " You can change with *Game Length* ";
                keyboardOptions.Add("Game Length");

                //Reset/redeal
                message += "\r\n- " + remainingQuestions.Count + " questions and " + remainingAnswers.Count + " answers remain in the deck. If you have added / removed packs from the filter, or you want to empty everyone's current hand, you can do this with *Re-deal*. To reset everything to default, and stop the game, use *Reset*";
                if (status != xyzzy_Statuses.Stopped) { message += " You can add more cards to the existing deck with *Extend*"; }
                keyboardOptions.Add("Re-deal");
                keyboardOptions.Add("Reset");
                keyboardOptions.Add("Extend");
                //timeouts / throttle
                message += "\r\n- " + maxWaitTimeHours + " hour timeouts before the game skips slow players. Change with *Timeout* ";
                keyboardOptions.Add("Timeout");
                message += "\r\n- Wait at least " + minWaitTimeHours + " hours between hands (i.e. force a slower game). Change with *Delay* ";
                keyboardOptions.Add("Delay");
                //kick
                message += "\r\n- " + "You can kick a player with *Kick*, or abandon the whole game with *Abandon*. *Mess With* will mess with a player's score, where as *Change Score* will change it permanantly";
                keyboardOptions.Add("Kick");
                keyboardOptions.Add("Abandon");
                keyboardOptions.Add("Mess With");
                keyboardOptions.Add("Change Score");
                //question
                message += "\r\n- " + "If the game gets stuck, you can try *Force Question* to move things along.";
                keyboardOptions.Add("Force Question");
                //bots ("Add Bots" - MIGRATION.md phase 9, legacy never had non-human players)
                message += "\r\n- " + "Short on people? *Add Bots* to pad out the game with computer-controlled players.";
                keyboardOptions.Add("Add Bots");

                //NB: this needs to be done inside the admin message, to prevent non-admin users getting past this point. Any other messages should be a normal sendMessage.
                Messaging.SendQuestion(chatID, m.userID, message, true, typeof(mod_xyzzy), "Settings", null, -1, true, TelegramAPI.createKeyboard(keyboardOptions, 2), true);
            }
            else
            {
                message += "\r\n You are not an admin in this group, so you'll need to get someone else to change the game settings. An admin can add you by typing /addadmin in the main group chat.";
                Messaging.SendMessage(m.userID, message, m.userFullName, true);
            }


            //chat settings
            //message += "\r\nNB: There are also a number of general chat settings that you can change using /settings in the group chat.";
            
            

        }

        public void sendSettingsMsgToChat()
        {

            string message = "";
            if (status == xyzzy_Statuses.Stopped)
            {
                message += "No game is running. You should probably start a new game by typing /xyzzy_start in your group chat.";
                if (players.Count > 1) { message += " You can also continue from the last game by typing /xyzzy_settings, and selecting *Extend*."; }

            }
            else
            {
                message += "Current settings are below. You can change with /xyzzy_settings, or use /xyzzy_status to get the current state of the game.";
                
                message += "\r\n- " + remainingQuestions.Count + " questions and " + remainingAnswers.Count + " answers remain in the deck";
                message += "\r\n- " + maxWaitTimeHours + " hour timeouts before the game skips slow players.";
                message += "\r\n- Wait at least " + minWaitTimeHours + " hours between hands starting.";
                message += "\r\n- " + packFilterIDs.Count + " packs currently enabled.";
                //add the enabled packs:
                message += "\r\n \r\n" + "Enabled Packs:" + "\r\n" + getPackFilterStatus();

            }
            Messaging.SendMessage(chatID, message);

        }

        public  void forceQuestion()
        {
            log("Forcing the next question", logging.loglevel.high);
            askQuestion(true);
        }

        public void extend()
        {
            addQuestions();
            addAllAnswers();

            Messaging.SendMessage(chatID, "Added additional cards to the game!");
            if (status == xyzzy_Statuses.Stopped && players.Count > 1)
            {
                Roboto.Settings.stats.logStat(new statItem("New Games Started",typeof(mod_xyzzy) ));
                askQuestion(true);
            }


        }

        public void askKickMessage(message m)
        {
            List<string> playernames = new List<string>();
            foreach (mod_xyzzy_player p in players ) { playernames.Add(p.name); }
            playernames.Add("Cancel");
            var keyboard = TelegramAPI.createKeyboard(playernames, 2);
            Messaging.SendQuestion(chatID, m.userID, "Which player do you want to kick", true, typeof(mod_xyzzy), "kick", m.userFullName, -1, true, keyboard);

        }

        public void reDeal()
        {
            //set the status (temporarily)
            setStatus(xyzzy_Statuses.Stopped);

            //clear the various cached lists
            foreach (mod_xyzzy_player p in players)
            {
                p.cardsInHand.Clear();
                p.selectedCards.Clear();
            }
            remainingAnswers.Clear();
            remainingQuestions.Clear();
            Messaging.clearExpectedReplies(chatID, typeof(mod_xyzzy));

            //now build back up
            addAllAnswers();
            addQuestions();
            askQuestion(true);
            
        }


        /// <summary>
        /// Calculate which players still need to responsd.
        /// </summary>
        /// <returns></returns>
        private List<mod_xyzzy_player> outstandingResponses()
        {
            List<mod_xyzzy_player> players = new List<mod_xyzzy_player>();

            List<ExpectedReply> expectedReplies = Messaging.getExpectedReplies(typeof(mod_xyzzy), chatID, -1, "Question");

            foreach (ExpectedReply r in expectedReplies)
            {
                mod_xyzzy_player player = getPlayer(r.userID);
                players.Add(player);
            }
            return players;
        }

        private void wrapUp()
        {
            Roboto.Settings.stats.logStat(new statItem("Games Ended", typeof(mod_xyzzy)));
            Messaging.clearExpectedReplies(chatID, typeof(mod_xyzzy));
            setStatus(xyzzy_Statuses.Stopped);
            String message = "Game over!";
            if (players.Count > 1) { message += " You can continue this game with the same players by selecting Extend in /xyzzy_settings"; }
            message += "\r\nScores are: ";
            foreach (mod_xyzzy_player p in players.OrderByDescending(x => x.wins))
            {
                message += "\r\n" + p.name + " - " + p.wins.ToString() + " points";
            }

            Messaging.SendMessage(chatID, message);
        }

        /// <summary>
        /// Check if all players have answered
        /// </summary>
        /// <returns></returns>
        internal bool allPlayersAnswered()
        {
            
            //TODO - remove the tzar from this!
            foreach (mod_xyzzy_player p in players)
            {
                if (p != players[lastPlayerAsked])
                {
                    //if so, have we got one? 
                    mod_xyzzy_card question = getLocalData().getQuestionCard(currentQuestion);
                    if (question != null)
                    {
                        if (p.selectedCards.Count < question.nrAnswers) { return false; }
                    }
                    else
                    {
                        log("Current question card not found!", logging.loglevel.critical);
                        askQuestion(false);
                    }
                }
            }
            return true;

        }

        public void askGameLength(message m)
        {
            Messaging.SendQuestion(chatID, m.userID, "How many questions do you want the round to last for (-1 for infinite)", true, typeof(mod_xyzzy), "SetGameLength");
        }

        internal void beginJudging(bool judgesMessageOnly = false)
        {

            if (players.Count < 2)
            {
                log("Abandoning game during judging phase - no players!", logging.loglevel.high);
                reset();
            }
            else
            {
                if (lastPlayerAsked > players.Count)
                {
                    log("Judge ID invalid during judging, resetting to first player.", logging.loglevel.high);
                    lastPlayerAsked = 0;
                }
                setStatus(xyzzy_Statuses.Judging);
                mod_xyzzy_coredata localData = getLocalData();

                mod_xyzzy_card q = localData.getQuestionCard(currentQuestion);
                mod_xyzzy_player tzar = players[lastPlayerAsked];

                int possibleAnswerCount = 0;

                if (q == null)
                {
                    log("Question Card not found - abandoning judging and skipping to next question", logging.loglevel.critical);
                    askQuestion(true);
                    return ;

                }


                //get all the responses for the keyboard, and the chat message
                List<string> responses = new List<string>();
                string chatMsg = "All answers received! The honourable " + tzar.name + " presiding." + "\r\n" +
                    "Question: " + q.text + "\r\n" + "\r\n";
                string missingRepliestxt = "Skipped these chumps: ";
                bool missingReplies = false;
                foreach (mod_xyzzy_player p in players)
                {
                    if (p != tzar)
                    {
                        //handle multiple answers for a question 
                        string answer = "";
                        foreach (string cardUID in p.selectedCards)
                        {
                            possibleAnswerCount++;
                            mod_xyzzy_card card = localData.getAnswerCard(cardUID);
                            if (answer != "") { answer += " >> "; }


                            if (card != null)
                            {
                                answer += card.text;
                            }
                            else
                            {
                                log("Player selected card with guid " + cardUID + " which was missing!", logging.loglevel.high);
                                answer += " Error - card missing ";
                            }
                            
                        }
                        if (answer != "")
                        {
                            responses.Add(answer);
                        }
                        else
                        {
                            missingReplies = true;
                            missingRepliestxt += "\r\n" + " - " + p.name;
                        }
                    }
                }
                responses.Sort(); //sort so that player order isnt same each time.

                foreach (string answer in responses) { chatMsg += "  - " + answer + "\r\n"; }

                if (missingReplies) { chatMsg += missingRepliestxt; }

                var keyboard = TelegramAPI.createKeyboard(responses, 1);
                //int judgeMsg = TelegramAPI.GetReply(tzar.playerID, "Pick the best answer! \r\n" + q.text, -1, true, keyboard);
                //localData.expectedReplies.Add(new mod_xyzzy_expectedReply(judgeMsg, tzar.playerID, chatID, ""));
                //TODO - add messageData types to an enum

                if (possibleAnswerCount > 0)
                {
                    if (tzar.isBot)
                    {
                        // "Add Bots" (MIGRATION.md phase 9) - a bot judge never receives a DM,
                        // it picks a random submitted answer immediately instead. responses is
                        // already each non-judge player's combined answer text, exactly what
                        // judgesResponse expects to match against. Still send the same "All
                        // answers received!" group announcement the human-judge path below sends
                        // (respecting judgesMessageOnly the same way) - found missing via a live
                        // user report: with a bot judge, the group went straight from "everyone's
                        // answered" to "a winner's been picked" with no announcement of what the
                        // answers even were in between, since this branch returned before ever
                        // reaching the human-judge path's chatMsg send below.
                        if (!judgesMessageOnly)
                        {
                            Messaging.SendMessage(chatID, chatMsg);
                        }
                        judgesResponse(responses[settings.getRandom(responses.Count)]);
                        return;
                    }

                    long messageID = Messaging.SendQuestion(chatID, tzar.playerID, "Pick the best answer! \r\n" + q.text, true, typeof(mod_xyzzy), "Judging", null, -1, true, keyboard);

                    if (messageID == long.MinValue)
                    {
                        log("Couldnt send judges message, probably blocked, maybe failure? ", logging.loglevel.high);
                    }
                    else if (messageID == -403)
                    {
                        log("Couldnt send judges message, blocked by user / user doesnt exist. Removing from game", logging.loglevel.warn);
                        Messaging.SendMessage(chatID, tzar.name + " has blocked the bot, which was particularly douchey of them. " + tzar.name + " is a douche. Or maybe a bag for them. Either way, they've been removed the from the game.");
                        removePlayer(tzar.playerID);
                    }

                    //Send the general chat message
                    else if (!judgesMessageOnly)
                    {
                        Messaging.SendMessage(chatID, chatMsg);
                    }
                }
                else
                {
                    Messaging.SendMessage(chatID, "Not enough answers to judge! Skipping to next question");
                    log("Not enough answers to judge! Skipping to next question ", logging.loglevel.warn);
                    askQuestion(true);
                }
            }
        }

        public void askChangeScoreMessage(message m)
        {
            List<string> playernames = new List<string>();
            foreach (mod_xyzzy_player p in players) { playernames.Add(p.name); }
            playernames.Add("Cancel");
            var keyboard = TelegramAPI.createKeyboard(playernames, 2);
            Messaging.SendQuestion(chatID, m.userID, "Whose score do you want to alter?", true, typeof(mod_xyzzy), "changescore", m.userFullName, -1, true, keyboard);
        }

        public void askFuckWithMessage(message m)
        {
            List< string > playernames = new List<string>();
            foreach (mod_xyzzy_player p in players) { playernames.Add(p.name); }
            playernames.Add("Cancel");
            var keyboard = TelegramAPI.createKeyboard(playernames, 2);
            Messaging.SendQuestion(chatID, m.userID, "Pick a player to toggle the Mess-With flag", true, typeof(mod_xyzzy), "fuckwith", m.userFullName, -1, true, keyboard);
        }

        /// <summary>"Add Bots" (MIGRATION.md phase 9) - reachable from the upfront Invites prompt,
        /// the Invites setup screen (manual "Add Bots" reply) and the /xyzzy_settings menu, which
        /// need different follow-ups once bots are actually added (mod_xyzzy.cs's "AddBotsCount " +
        /// returnContext reply handler), so the caller's own context ("InvitesUpfront"/"Invites"/
        /// "Settings") rides along in messageData. promptText lets the upfront ask use its own
        /// wording instead of the generic retry/manual-invocation one.</summary>
        public void askAddBotsCount(message m, string returnContext, string promptText = "How many bots would you like to add?")
        {
            var keyboard = TelegramAPI.createKeyboard(new List<string> { "1", "2", "3", "5", "Remove All Bots", "Cancel" }, 2);
            Messaging.SendQuestion(chatID, m.userID, promptText, true, typeof(mod_xyzzy), "AddBotsCount " + returnContext, m.userFullName, -1, true, keyboard);
        }

        /// <summary>The final, "click Start when ready" step of game setup - kept deliberately clean
        /// of the "Add Bots" button now that bot-adding is offered upfront as its own step
        /// (askAddBotsCount with returnContext "InvitesUpfront", called right before this) - see
        /// MIGRATION.md. Still reachable by manually typing "Add Bots" (mod_xyzzy.cs's "Invites"
        /// handler), for players who decided against bots upfront but change their mind.</summary>
        public void sendInvitesStartMessage(message m, bool notEnoughPlayersYet = false)
        {
            string text = (notEnoughPlayersYet ? "Not enough players yet. " : "")
                + "To start the game once enough players have joined click the \"Start\" button below. "
                + "You will need three or more players to start the game, or it will be topped up with bots.";
            var keyboard = TelegramAPI.createKeyboard(new List<string> { "Start", "Cancel" }, 2);
            Messaging.SendQuestion(chatID, m.userID, text, true, typeof(mod_xyzzy), "Invites", m.userFullName, -1, true, keyboard);
        }





        /// <summary>
        /// Gets the status of the current game
        /// </summary>
        public void getStatus()
        {
            string response = "";
            TimeSpan quietHoursStart = TimeSpan.MinValue;
            TimeSpan quietHoursEnd = TimeSpan.MinValue;
            mod_standard.getQuietTimes(chatID, out quietHoursStart, out quietHoursEnd);
            TimeSpan currentTime = new TimeSpan(DateTime.Now.Hour, DateTime.Now.Minute, DateTime.Now.Second);
            DateTime throttleTimeStart = lastHandStartedTime.Add(new TimeSpan(minWaitTimeHours, 0, 0));

            if ( (quietHoursStart > quietHoursEnd && (currentTime > quietHoursStart || currentTime < quietHoursEnd ))
                || (quietHoursStart < quietHoursEnd && (currentTime > quietHoursStart && currentTime < quietHoursEnd)))
            {
                response += "Shhh... its sleepy times" + "\r\n";
            }
            

            if (status == xyzzy_Statuses.waitingForNextHand )
            {
                response = "Waiting for next hand to start. ";
                DateTime nextHand = DateTime.MinValue;
                

                //we are either throttled, or waiting for the quiet hours to run out. 
                if (mod_standard.isTimeInQuietPeriod(chatID, DateTime.Now))
                {
                    response += "Chat quiet time finishes at " + quietHoursEnd.ToString("c") + ".";                    
                }
                //do we have a throttle set? If so, are we within the window? 
                if (minWaitTimeHours > 0 && throttleTimeStart > DateTime.Now)
                {
                    response += "Next hand won't start until " + throttleTimeStart.ToString() + " (" + minWaitTimeHours.ToString() + " hours between hands.)";
                }

            }


            response += " The current status of the game is " + status.ToString() + ".";
            if (status == xyzzy_Statuses.Stopped)
            {
                response += " Type [/xyzzy_start](/xyzzy_start) to begin setting up a new game.";

            }
            else
            {

                if ((status == xyzzy_Statuses.Judging || status == xyzzy_Statuses.Question) && maxWaitTimeHours != 0)
                {
                    DateTime expiresTime = Helpers.common.addTimeIgnoreQuietHours(statusChangedTime, quietHoursStart, quietHoursEnd, new TimeSpan(maxWaitTimeHours, 0, 0));
                    TimeSpan remainingTime = expiresTime.Subtract(DateTime.Now);
                    response += " and " +
                        (remainingTime.Days > 0 ? remainingTime.Days + " days " : "") +
                        (remainingTime.Hours > 0 ? remainingTime.Hours + " hours " : "") +
                        (remainingTime.Minutes > 0 ? remainingTime.Minutes + " minutes " : "") +
                        "left to answer!";
                }

                if (status == xyzzy_Statuses.Judging || status == xyzzy_Statuses.Question || status == xyzzy_Statuses.waitingForNextHand)
                {
                    response += " There are " + remainingQuestions.Count.ToString() + " questions remaining";

                    response += " Say [/xyzzy_join](/xyzzy_join) to join.";
                    response += " The following players are currently playing: \r\n";
                    //order the list of players
                    List<mod_xyzzy_player> orderedPlayers = players.OrderByDescending(e => e.wins).ToList();

                    foreach (mod_xyzzy_player p in orderedPlayers) { response += p.getPointsMessage(); }
                    response += " \r\n";
                }

                switch (status)
                {
                    case xyzzy_Statuses.Question:
                        // getQuestionCard(currentQuestion) can return null here - real production
                        // crash found live (/xyzzy_status during a Question round, no response sent
                        // at all - the card the round is currently asking had gone missing from the
                        // catalog, e.g. its pack got dropped/disabled mid-game). Guarded rather than
                        // assumed present, matching the same "card may not exist any more" reality
                        // askQuestion()'s own empty-pool guard already treats as real, not
                        // hypothetical.
                        mod_xyzzy_card currentQuestionCard = getLocalData().getQuestionCard(currentQuestion);
                        if (currentQuestionCard == null)
                        {
                            response += "The current question card is no longer available (its pack may have been removed) - use /xyzzy_settings to check your pack filters.";
                            break;
                        }
                        response += "The current question is : " + "\r\n" +
                            Helpers.common.escapeMarkDownChars(currentQuestionCard.text) + "\r\n" +
                            "Still waiting on the following players :";
                        bool unsentMessages = false;
                        bool first = true;
                        foreach (ExpectedReply r in Messaging.getExpectedReplies(typeof(mod_xyzzy), chatID, -1, "Question"))
                        {
                            if (r.chatID == chatID)
                            {
                                mod_xyzzy_player p = getPlayer(r.userID);
                                if (p != null)
                                {
                                    response += (first? " " : ", ") + p.ToString(true);
                                    if (first) { first = false; }
                                    if (!r.isSent())
                                    {
                                        response += "(*)";
                                        unsentMessages = true;
                                    }

                                }
                            }
                            response += "\r\n";
                        }
                        if (unsentMessages) { response += "(\\*) These messages have not yet been sent, as I am waiting for a reply to another question!"; }

                        break;

                    case xyzzy_Statuses.Judging:
                        response += "Waiting for " + players[lastPlayerAsked].name_markdownsafe +  " to judge";
                        break;
                    case xyzzy_Statuses.cardCastImport:
                    case xyzzy_Statuses.Invites:
                    case xyzzy_Statuses.SetGameLength:
                    case xyzzy_Statuses.setPackFilter:
                    case xyzzy_Statuses.setMaxHours:
                    case xyzzy_Statuses.setMinHours:
                        response += players[0].name_markdownsafe + " is currently setting the game up - type [/xyzzy_join](/xyzzy_join) to join in!";

                        break;
                }
            }


            long messageID = Messaging.SendMessage(chatID, response, null, true, -1 , true);
            if (messageID == -403)
            {
                log("Bot blocked - abandoning", logging.loglevel.high);
                reset();
            }
            else
            {
                check();
            }
        }

        /// <summary>
        /// Set the timeout value
        /// </summary>
        /// <param name="text_msg"></param>
        /// <returns></returns>
        public bool setMaxTimeout(string text_msg)
        {
            if (text_msg == "Continue") { return true; }
            else if (text_msg == "No Timeout")
            {
                maxWaitTimeHours = 0;
                return true;
            }
            else
            {
                int hours = -1;
                bool success = int.TryParse(text_msg, out hours);
                if (!success || hours < 0 || hours > 168) { return false; }
                else
                {
                    maxWaitTimeHours = hours;
                    return true;
                }

           }
        }

        /// <summary>
        /// Ask the user what the timeout value should be
        /// </summary>
        /// <param name="userID"></param>
        public void askMaxTimeout(long userID)
        {
            var kb = TelegramAPI.createKeyboard(new List<string>()
            {
                "Continue", "No Timeout"
                , "1","2", "6", "12", "24", "48"
            }, 2);

            Messaging.SendQuestion(chatID, userID, "Do you want to set a timeout? Enter how long (in hours) before someone is skipped, or 'Continue' to accept the last value ("
                + (maxWaitTimeHours == 0 ? "No Timeout" : maxWaitTimeHours.ToString()) + ")"
                , true, typeof(mod_xyzzy), "setMaxHours", null, -1, true, kb );
        }

        /// <summary>
        /// Ask the user what the throttle should be set to
        /// </summary>
        /// <param name="userID"></param>
        public void askMinTimeout(long userID)
        {

            var kb = TelegramAPI.createKeyboard(new List<string>()
            {
                "Continue", "No Limit"
                , "1","2", "6", "12", "24", "48"
            }, 2);

            Messaging.SendQuestion(chatID, userID, "Do you want to force a slower game? Enter how long (in hours) before a new round can start, or 'Continue' to accept the last value ("
                + (minWaitTimeHours == 0 ? "No Limit" : minWaitTimeHours.ToString()) + ")"
                , true, typeof(mod_xyzzy), "setMinHours", null, -1, true, kb);
        }

        /// <summary>
        /// Set the timeout value
        /// </summary>
        /// <param name="text_msg"></param>
        /// <returns></returns>
        public bool setMinTimeout(string text_msg)
        {
            if (text_msg == "Continue") { return true; }
            else if (text_msg == "No Limit")
            {
                minWaitTimeHours = 0;
                return true;
            }
            else
            {
                int hours = -1;
                bool success = int.TryParse(text_msg, out hours);
                if (!success || hours < 0 || hours > 168) { return false; }
                else
                {
                    minWaitTimeHours = hours;
                    return true;
                }

            }
        }

        internal void toggleFuckWith(long playerID)
        {
            mod_xyzzy_player p =  getPlayer(playerID);
            if (p != null)
            {
                p.toggleFuckWith();
            }
        }


        /// <summary>
        /// A judge has replied to their PM asking to vote for the winning answer.
        /// </summary>
        /// <param name="p"></param>
        internal bool judgesResponse(string chosenAnswer)
        {
            mod_xyzzy_coredata localData = getLocalData();
            mod_xyzzy_card q = localData.getQuestionCard(currentQuestion);
            mod_xyzzy_player tzar = players[lastPlayerAsked];
            mod_xyzzy_player winner = null;

            //sometimes responses get mangled - think this is the telegram desktop client being clever. Replace the char. 
            chosenAnswer = chosenAnswer.Replace("»", ">>");

            //find the response that matches
            string possiblematches = "";

            foreach (mod_xyzzy_player p in players)
            {
                if (p != tzar) //dont check the judge for submitted answers! Prevent issue with them entering a symbol and comparing it to an empty answer. 
                {
                    //handle multiple answers for a question 
                    string answer = "";

                    foreach (string cardUID in p.selectedCards)
                    {

                        mod_xyzzy_card card = localData.getAnswerCard(cardUID);
                        if (answer != "")
                        {
                            answer += " >> ";
                        }
                        answer += card.text;

                    }
                    possiblematches += answer + ", ";

                    //Keyboard seems to trim the answers at about 110 chars, so ignore anything after that point. 
                    //cleanse text and match
                    //if ( answer.Substring(0, Math.Min(answer.Length, 100)) == chosenAnswer.Substring(0, Math.Min(chosenAnswer.Length, 100)))
                    //if (answer == chosenAnswer)

                    //exact match
                    //OR First 100 match - for lenny faces  ( ͡° ͜ʖ ͡°) 
                    //OR cleansed strings match 

                    if (answer == chosenAnswer
                        || (answer.Length > 100 ? answer.Substring(0, 100) : answer) == (chosenAnswer.Length > 100 ? chosenAnswer.Substring(0, 100) : chosenAnswer)
                        || Helpers.common.cleanseText(answer, 100) == Helpers.common.cleanseText(chosenAnswer, 100))
                    {
                        winner = p;
                    }
                }
            }

            if (winner != null)
            {
                //give the winning player a point. 
                winner.wins++;
                string message = winner.name_markdownsafe + " wins a point!\r\n";
                
                //try and insert the answers into the message. 
                bool formattedQuestionSuccessfully = false;
                try

                {
                    Regex insertPoints = new Regex(@"_+");
                    MatchCollection mc = insertPoints.Matches(q.text);
                    string formattedquestion = "";
                    int origstringpos = 0;

                    if (mc.Count == winner.selectedCards.Count() && mc.Count == q.nrAnswers)
                    {
                        int answernr = 0;
                        foreach (Match m in mc)
                        {
                            //add the text up to this point
                            formattedquestion += q.text.Substring(origstringpos, m.Index - origstringpos);

                            // Imported pack data is inconsistent about whether a blank already has
                            // spaces around it - "I found ___ when I was cleaning" (fine as-is) vs
                            // "I found_when I was cleaning" (glues the answer straight onto the
                            // surrounding words once substituted). Only add a space where the
                            // neighbouring character is a letter/digit glued directly onto the
                            // blank with nothing in between - an already-spaced card is untouched
                            // (no double space), and a blank immediately before punctuation (e.g.
                            // "I like to ___.") doesn't get a space wrongly inserted before the
                            // period, since punctuation isn't a letter/digit.
                            if (formattedquestion.Length > 0 && char.IsLetterOrDigit(formattedquestion[formattedquestion.Length - 1]))
                            {
                                formattedquestion += " ";
                            }

                            //add the answer
                            formattedquestion += "*" + localData.getAnswerCard(winner.selectedCards[answernr]).text + "*";

                            //move our indexes along.
                            origstringpos = m.Index + m.Length;
                            answernr++;

                            if (origstringpos < q.text.Length && char.IsLetterOrDigit(q.text[origstringpos]))
                            {
                                formattedquestion += " ";
                            }
                        }
                        //spool the end of the string
                        formattedquestion += q.text.Substring(origstringpos);
                    }
                    else
                    {
                        log("Nr matches (" + mc.Count + ") != winner selected cards (" + winner.selectedCards.Count() + ") != nr answers on question (" + q.nrAnswers + ")", logging.loglevel.warn);
                    }

                    message += formattedquestion;
                    formattedQuestionSuccessfully = true;

                }
                catch (Exception e)
                {
                    log("Error trying to format answer with responses " + e.ToString() + ". Falling back to before/after mode", logging.loglevel.warn);
                }
                //fallback - if we cant figure out where, just add them on the end. 
                if (formattedQuestionSuccessfully == false)
                {
                    log("Fallback mode for winners message", logging.loglevel.normal);
                    message += "Question: " + q.text + "\r\nAnswer:" + chosenAnswer + "\r\nThere are " + remainingQuestions.Count.ToString() + " questions remaining. Current scores are: ";
                }
            
                //output the current scores    
                List<mod_xyzzy_player> orderedPlayers = players.OrderByDescending(e => e.wins).ToList();
                foreach (mod_xyzzy_player p in orderedPlayers)
                {
                    message += p.getPointsMessage();
                    //message += "\r\n" + p.name_markdownsafe + " - " + p.wins.ToString() + " points";
                }

                Messaging.SendMessage(chatID, message, null ,  true);

                //ask the next question (will jump to summary if no more questions). 
                askQuestion(false);
            }
            else
            {
                //answer not found?
                Roboto.Settings.stats.logStat(new statItem("Bad Responses", typeof(mod_xyzzy)));
                Messaging.SendMessage(tzar.playerID, "Couldnt find your answer, try again?");

                log("Couldn't match judges response '" + chosenAnswer
                + "' in chat " + chatID
                + " which has question " + currentQuestion + " ("
                + (q.text.Length > 10 ? q.text.Substring(0, 10) : q.text)
                + ") out - probably an invalid response", logging.loglevel.warn);
                log("Possible answers for judge were: " + possiblematches, logging.loglevel.verbose);

                beginJudging(true);
                return false;
            }
            return true;
        }

        public bool setPlayerScore(long playerID, int playerScore)
        {
            mod_xyzzy_player p = getPlayer(playerID);
            bool success = p.setScore(playerScore);
            if (success)
            {
                Messaging.SendMessage(chatID, "Player " + p.ToString() + "'s score has been changed to " + playerScore);
            }
            else
            {
                log("Score change failed for some reason", logging.loglevel.high);
            }
            return success;
            
        }

        public override void startupChecks()
        {
            
            int i = packFilterIDs.Count();
            if (i > 250) //i.e enough that its stupid
            {
                packFilterIDs.Clear();
                packFilterIDs.Add(mod_xyzzy.AllPacksEnabledID);
                log("Replaced large filter list with allPacks flag ", logging.loglevel.high);
            }

            packFilterIDs = packFilterIDs.Distinct().ToList();
            log("Removed " + (i - packFilterIDs.Count()) + " filters (now " + packFilterIDs.Count() + ")", logging.loglevel.verbose);
            
        }

        /// <summary>
        /// Check consistency of game state
        /// </summary>
        internal void check(bool fullCheck = false)
        {
            log("Performing " + (fullCheck?"Full":"Quick") + " status check for " + chatID + " " + getChat().chatTitle + " - status=" + status.ToString() +  ".", logging.loglevel.low);

            statusMiniCheckedTime = DateTime.Now;
            if (fullCheck) { statusCheckedTime = DateTime.Now; }


            //mod_xyzzy_coredata localData = getLocalData();
            List<ExpectedReply> replies = Messaging.getExpectedReplies(typeof(mod_xyzzy), chatID);
            List<ExpectedReply> repliesToRemove = new List<ExpectedReply>();

            //find out if our chat has a quiet time set
            TimeSpan quietStartTime = TimeSpan.Zero;
            TimeSpan quietEndTime = TimeSpan.Zero;
            mod_standard.getQuietTimes(chatID, out quietStartTime, out quietEndTime);

            //Timeout Reminders
            //workout the time at which we should send reminders
            float dur = maxWaitTimeHours * 60; 
            DateTime reminderTime = Helpers.common.addTimeIgnoreQuietHours(statusChangedTime, quietStartTime, quietEndTime, new TimeSpan(0, Convert.ToInt32(dur * 0.75), 0));  //statusChangedTime.AddMinutes((maxWaitTimeHours * 60) * .75);
            DateTime abandonTime = Helpers.common.addTimeIgnoreQuietHours(statusChangedTime, quietStartTime, quietEndTime, new TimeSpan(0, Convert.ToInt32(dur), 0));


            if (fullCheck)
            {

                //does the group still exist? Is the bot still in it?
                int chatMemberCount = TelegramAPI.getChatMembersCount(chatID);
                log("There are " + chatMemberCount + " people in current group", logging.loglevel.verbose);
                if (chatMemberCount <= 1)
                {
                    if (chatMemberCount == 1) //everyone but the bot has left
                    {
                        log("Everyone has left group " + chatID + " " + getChat().chatTitle + " - abandoning game", logging.loglevel.warn);
                        //cancel the game, clear any expected replies
                        reset();
                    }

                    if (chatMemberCount == -403) //forbidden, i.e. bot kicked from group
                    {
                        log("Bot has been kicked from " + chatID + " " + getChat().chatTitle + " - abandoning game", logging.loglevel.warn);
                        //cancel the game, clear any expected replies
                        reset();
                    }


                }
            }

            //is the tzar valid?
            if (lastPlayerAsked >= players.Count)
            {
                lastPlayerAsked = 0;
                log("Reset tzar as ID invalid.");
            }

            //are we out of players? 
            if ((status == xyzzy_Statuses.Judging || status == xyzzy_Statuses.Question) && players.Count < 2)
            {
                log("Stopping game, not enough players");
                Messaging.SendMessage(chatID, "Stopping game, not enough players");
                wrapUp();
            }

            //responses from non-existent players, or replies for stopped games
            foreach (ExpectedReply reply in Messaging.getExpectedReplies(typeof(mod_xyzzy), chatID ))
            {
                if (reply.chatID == chatID)
                {
                    if (getPlayer(reply.userID) == null) //player removed
                    {
                        repliesToRemove.Add(reply);
                    }

                }
            }
            foreach (ExpectedReply r in repliesToRemove)
            {
                Messaging.removeReply(r);
                log(" Removed expected reply " + r.userID + "\\" + r.pluginType.ToString() + "\\" + r.messageData + ".");
            }


            //do we have any duplicate cards? rebuild the list
            int count_q = remainingQuestions.Count;
            int count_a = remainingAnswers.Count;
            remainingQuestions = remainingQuestions.Distinct().ToList();
            remainingAnswers = remainingAnswers.Distinct().ToList();

            //duplicate strings in packfilter?
            packFilterIDs = packFilterIDs.Distinct().ToList();

            //current status
            switch (status)
            {
                case xyzzy_Statuses.Question:
                    if (allPlayersAnswered())
                    {
                        beginJudging();
                    }
                    else if (replies.Count() == 0)
                    {
                        //something went wrong, we are missing someone's expectedReply...
                        log("ExpectedReply missing - skipping to judging", logging.loglevel.high);
                        beginJudging();
                    }

                    //timeout players who havent answered
                    if (maxWaitTimeHours > 0 && DateTime.Now > abandonTime)
                    {
                        List<mod_xyzzy_player> outstanding = outstandingResponses(); ;
                        string outstandingPlayers = "";
                        int i = 0;
                        foreach (mod_xyzzy_player p in outstanding)
                        {
                            i++;
                            //first
                            if (i == 1) { outstandingPlayers = p.ToString(); }
                            //last
                            else if (i == outstanding.Count()) { outstandingPlayers += " and " + p.ToString(); }
                            //middle
                            else { outstandingPlayers += ", " + p.ToString(); }
                        }
                        Messaging.SendMessage(chatID, outstandingPlayers + " can suck it for not answering in time:");

                        log("Skipping to judging for chat" + chatID);
                        beginJudging();
                    }
                    //timeouts, if we have questions outstanding
                    else if (remindersSent == false && maxWaitTimeHours > 0 && DateTime.Now > reminderTime)
                    {
                        log("Sending reminders for chat " + chatID);
                        //check if any players are late
                        string outstandingPlayers = "";
                        List<mod_xyzzy_player> outstanding = outstandingResponses(); ;
                        int i = 0;

                        if (outstanding.Count == 0)
                        {
                            log("Couldnt send reminders, as no outstanding players!?", logging.loglevel.high);
                        }
                        else
                        {
                            foreach (mod_xyzzy_player p in outstanding)
                            {
                                i++;
                                //first
                                if (outstandingPlayers == "") { outstandingPlayers = p.ToString(); }
                                //last
                                else if (i == outstanding.Count()) { outstandingPlayers += " and " + p.ToString(); }
                                //middle
                                else { outstandingPlayers += ", " + p.ToString(); }
                            }
                            if (outstanding.Count == 1)
                            { Messaging.SendMessage(chatID, outstandingPlayers + " needs to hurry up! Tick-tock..."); }
                            else
                            { Messaging.SendMessage(chatID, outstandingPlayers + " need to hurry up! Tick-tock..."); }
                        }

                        remindersSent = true;
                    }
                    
                    
                    break;
                case xyzzy_Statuses.Judging:
                    //check if there is an appropriate expected reply
                    
                    bool reask = false;
                    if (players.Count() == 0 )
                    {
                        reset();
                    }
                    else if (replies.Count > 1)
                    {
                        Messaging.clearExpectedReplies(chatID, typeof(mod_xyzzy));
                        reask = true;
                        log("Cleared multiple expected replies during judging from game " + chatID.ToString(), logging.loglevel.high);

                    }
                    else if (replies.Count == 0)
                    {
                        reask = true;
                        log("No expected replies during judging from game " + chatID.ToString(), logging.loglevel.high);
                    }
                    else if (replies.Count == 1 && (replies[0].messageData != "Judging" || replies[0].userID != players[lastPlayerAsked].playerID))
                    {
                        Messaging.clearExpectedReplies(chatID, typeof(mod_xyzzy));
                        reask = true;
                        log("Removed invalid expected reply during judging from game " + chatID.ToString(), logging.loglevel.high);
                    }

                    if (reask)
                    {
                        if (statusChangedTime < DateTime.Now.Subtract(TimeSpan.FromDays(30))) 
                        {
                            log("Dormant game, abandoning", logging.loglevel.critical);
                            reset();
                        }
                        else
                        {
                            beginJudging(true);
                            log("Redid judging", logging.loglevel.critical);
                        }
                    }
                    else
                    {

                        //send reminder to judge
                        if (maxWaitTimeHours > 0 && remindersSent == false && DateTime.Now > reminderTime)
                        {
                            log("Sending judge reminder for chat " + chatID);

                            Messaging.SendMessage(chatID, "Hurry up Judgy Judgerson! (" + players[lastPlayerAsked].ToString() + ")");
                            remindersSent = true;
                        }
                        //abandon judging, too slow
                        else if (maxWaitTimeHours > 0 && DateTime.Now > abandonTime)
                        {
                            log("Skipping judging for chat" + chatID);

                            Messaging.SendMessage(chatID, "Judge was too slow, " + players[lastPlayerAsked].ToString() + " gets docked a point!");
                            players[lastPlayerAsked].wins--;
                            askQuestion(false);
                        }
                    }

                    

                    break;

                case xyzzy_Statuses.waitingForNextHand:
                    //handle where we have paused the game due to quiet hours and / or throttling

                    //make sure we arent in a quiet period
                    if (mod_standard.isTimeInQuietPeriod(chatID, DateTime.Now))
                    {
                        log("Still in quiet hours", logging.loglevel.verbose);
                    }
                    //are we still throttled?
                    else if (minWaitTimeHours != 0 && DateTime.Now < lastHandStartedTime.Add(new TimeSpan(minWaitTimeHours, 0, 0)))
                    {
                        log("Still throttled", logging.loglevel.verbose);
                    }
                    else
                    {
                        log("Resuming game", logging.loglevel.verbose);
                        askQuestion(false);
                    }

                    break;

                //for setup statuses, if we are idle for > 24h then kill the game. 
                case xyzzy_Statuses.cardCastImport:
                case xyzzy_Statuses.Invites:
                case xyzzy_Statuses.SetGameLength:
                case xyzzy_Statuses.setMaxHours:
                case xyzzy_Statuses.setMinHours:
                case xyzzy_Statuses.setPackFilter:
                case xyzzy_Statuses.useDefaults:
                    DateTime cutoff = DateTime.Now.Subtract(TimeSpan.FromDays(1));
                    if (statusChangedTime < cutoff)
                    {
                        reset();
                    }
                    break;


                case xyzzy_Statuses.Stopped:
                    //do we have any game related ERs outstanding? 
                    Messaging.clearExpectedReplies(chatID, typeof(mod_xyzzy));

                    break;
            }
            
        }
        

        public void replaceCard(mod_xyzzy_card old, mod_xyzzy_card newcard, string cardType)
        {
            String text = "Replacing " + cardType + "card " + old.text + " with " + newcard.text + " in " + getChat().chatID + "." ;
            string actions = "";
            if (cardType == "Q")
            {

                bool removed = remainingQuestions.Remove(old.uniqueID);
                if (removed)
                {
                    remainingQuestions.Add(newcard.uniqueID);
                    actions += " Replaced in remainingQs list.";
                }

                if (currentQuestion == old.uniqueID)
                {
                    currentQuestion = newcard.uniqueID;
                    actions += " Replaced current Question.";
                }
            }
            else
            {

                bool removed = remainingAnswers.Remove(old.uniqueID);
                if (removed)
                {
                    remainingAnswers.Add(newcard.uniqueID);
                    actions += " Replaced card in remainingAnswers.";
                }

                foreach (mod_xyzzy_player p in players)
                {
                    removed = p.cardsInHand.Remove(old.uniqueID);
                    if (removed)
                    {
                        actions += " Replaced card in " + p.name + "'s hand";
                        p.cardsInHand.Add(newcard.uniqueID);
                    }

                    removed = p.selectedCards.Remove(old.uniqueID);
                    if (removed)
                    {
                        actions += " Replaced selectedcard by " + p.name;
                        p.selectedCards.Add(newcard.uniqueID);
                    }
                }

            }
            if (actions != "")
            {
                log(text + actions, logging.loglevel.normal);
            }
        }

        /// <summary>
        /// Reset the player scores
        /// </summary>
        public void resetScores()
        {
            foreach (mod_xyzzy_player p in players) { p.wins = 0; }
        }

        /// <summary>
        /// Ask the organiser which packs they want to play with. Option to continue, or to select all packs. 
        /// By default, assume that the message contains the packname. Can be overwridden by the overload. 
        /// </summary>
        /// <param name="m"></param>
        internal void processPackFilterMessage(message m)
        {
            processPackFilterMessage(m, m.text_msg);
        }

        /// <summary>
        /// Ask the organiser which packs they want to play with. Option to continue, or to select all packs. 
        /// </summary>
        /// <param name="m"></param>
        /// <param name="packName"></param>
        internal void processPackFilterMessage (message m, string packName )
        { 
            mod_xyzzy_coredata localData = getLocalData();

            List<Helpers.cardcast_pack> matchingPacks = localData.getPackFilterList().Where(x => (x.name == packName.Substring(2)) || x.name == packName).ToList(); //remove the first two chars, as they are the emoji and a space (uinless its just been imported)

            //did they actually give us an answer? 
            if (m.text_msg == "All")
            {
                packFilterIDs.Clear();

                packFilterIDs.Add(mod_xyzzy.AllPacksEnabledID);
                /*NOOOPE - use a placeholder instead foreach(Helpers.cardcast_pack pack in localData.getPackFilterList())
                {
                    packFilterIDs.Add(pack.packID);
                }*/

                //packFilter.AddRange(localData.getPackFilterList());
            }
            else if (m.text_msg == "None")
            {
                packFilterIDs.Clear();
            }
            else if (matchingPacks.Count == 0 )//      .Contains(packName))
            {
                Messaging.SendMessage(m.chatID, "Not a valid pack!", m.userFullName,  false, m.message_id);
            }
            else
            {
                //get the pack 
                Helpers.cardcast_pack chosenPack = matchingPacks[0];

                //toggle the pack
                if (packFilterIDs.Contains(chosenPack.packID))
                {
                    packFilterIDs.Remove(chosenPack.packID);
                }
                else
                {
                    packFilterIDs.Add(chosenPack.packID);
                    chosenPack.picked();
                }
            }

        }

        public enum packAction {add, remove, toggle };

        /// <summary>
        /// set or toggle a pack filter in the current chat
        /// </summary>
        /// <param name="packID"></param>
        /// <param name="action"></param>
        /// <returns></returns>
        public int setPackFilter(Guid packID, packAction action)
        {
            switch (action)
            {
                //slightly iffy syntax. No "break" line as the case lines are returning the values directly. 
                case packAction.add:
                    if (packFilterIDs.Contains(packID)) { return 0; }
                    else { packFilterIDs.Add(packID); return 1; }
                    //break;
                case packAction.remove:
                    return packFilterIDs.RemoveAll(x => x == packID);
                    //break;
                case packAction.toggle:
                    if (packFilterIDs.Contains(packID)) { return packFilterIDs.RemoveAll(x => x == packID); }
                    else { packFilterIDs.Add(packID); return 1; }
                    //break;

            }
            return -1;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="m"></param>
        /// <param name="pageNr">1-based index of the page to display</param>
        public void sendPackFilterMessage(message m, int pageNr)
        {
            mod_xyzzy_coredata localData = getLocalData();
            String response = "The following packs (and their current status) are available. You can toggle the packs using the keyboard "
                + "below, or click 'Continue' to carry on. You can also import packs from CRCast by clicking 'Import Pack'";

            
            // Ceiling division, not (count / maxPacksPerPage) + 1 - the old formula produced a
            // phantom empty trailing page whenever the pack count was an exact multiple of
            // maxPacksPerPage (e.g. exactly 30 packs -> page 2 of 2 with zero pack buttons on it).
            // Confirmed present in real legacy; carried forward as a fix, not reproduced, per
            // MIGRATION.md phase 9's "pagination fix" delta - Math.Max guards the zero-packs case
            // (0 packs should still show page 1 of 1, not 1 of 0).
            int totalPageCount = Math.Max(1, (localData.getPackFilterList().Count() + maxPacksPerPage - 1) / maxPacksPerPage);
            

            //is our pageNr valid? 
            if (pageNr > totalPageCount)
            {
                log("PageNr of " + pageNr + " is greater than nr of pages " + totalPageCount, logging.loglevel.warn);
                pageNr = totalPageCount;
            }
            if (pageNr <1)
            {
                log("PageNr of " + pageNr + " is too low", logging.loglevel.warn);
                pageNr = 1;
            }

            //Now build up keybaord
            List<String> keyboardResponse = new List<string> { "Import Pack", "All", "None" };
            if (totalPageCount > 1)
            {

                if (pageNr > 1) { keyboardResponse.Add("Prev"); }
                if (pageNr < totalPageCount) { keyboardResponse.Add("Next"); }
            }

            //get an ordered list, starting at the current page. 
            int startAt = (pageNr - 1) * maxPacksPerPage;
            string currentStatus = "null";  //can be null, ON or OFF. Used for setting headings as we want to order by status.

            foreach (Helpers.cardcast_pack pack in localData.getPackFilterList().OrderByDescending(x => packEnabled(x.packID)).ThenBy(x => x.name).Skip(startAt).Take(maxPacksPerPage).ToList()) 
            {
                string packIsEnabled = packEnabled(pack.packID).ToString();
                if (currentStatus != packIsEnabled)
                {
                    currentStatus = packIsEnabled;
                    //write heading
                    if (currentStatus == "True")
                    {
                        response += "\r\n*Active Packs:*\r\n";
                    }
                    else
                    {
                        response += "\r\n*Inactive Packs:*\r\n";
                    }
                }

                //add message to the response
                string emoji = "";
                if (packEnabled(pack.packID)) { emoji = emoji_check; }
                else { emoji = emoji_cross; }
                string cleanPackName = Helpers.common.removeMarkDownChars(pack.name);
                response += emoji + cleanPackName + "\r\n";
            
                //add item to the keyboard
                keyboardResponse.Add(emoji + " " + pack.name);
            }

            //paging
            if (totalPageCount > 1)
            {
                response += "(Page " + pageNr + " of " + totalPageCount + ")";
            }

            //now send the new list. "Continue" is appended as its own trailing row (rather than
            //chunked in with everything else) so it always lands alone on the last row, spanning
            //both columns, instead of being buried as the first, easy-to-miss button.
            var keyboard = TelegramAPI.createKeyboard(keyboardResponse, 2);//todo columns
            keyboard.Add(new List<string> { "Continue" });
            Messaging.SendQuestion(chatID, m.userID, response, true, typeof(mod_xyzzy), "setPackFilter " + pageNr, m.userFullName,  -1, false, keyboard, true);
        }


        /// <summary>packFilterIDs starts empty (see its own field-initializer comment) rather than
        /// via a static default, since that can't safely consult the pack catalog at construction
        /// time. Resolves the real default the first time it's actually needed and caches it in
        /// place - packFilterIDs then persists normally from then on, same as an explicit choice, so
        /// this only ever runs once per chat - guarded by packFilterResolved rather than just
        /// checking packFilterIDs.Count == 0, since an empty list is also the genuinely correct,
        /// deliberate result of a player tapping "None" (disable every pack) - relying on Count alone
        /// would silently re-fill it the very next time anything calls packEnabled() (e.g.
        /// redisplaying the pack list right after "None" itself). Prefers "CAHBS" by pack code if a
        /// real one exists (a real bot's own persisted pack, stable since long before this ran),
        /// otherwise falls back to AllPacksEnabledID - which also naturally covers just the seeded
        /// "ZZ Dummy Pack" on a fresh instance, without needing to name it specifically or clean
        /// anything up once it's dropped.
        /// TODO: no admin-facing way to change this default yet - worth a settings option later.</summary>
        private void ensureDefaultPackFilter()
        {
            if (packFilterResolved) { return; }
            packFilterResolved = true;
            if (packFilterIDs.Count > 0) { return; }
            Helpers.cardcast_pack cahbs = getLocalData().getPackByCode("CAHBS");
            packFilterIDs.Add(cahbs != null ? cahbs.packID : mod_xyzzy.AllPacksEnabledID);
        }

        public bool packEnabled(Guid packID)
        {
            ensureDefaultPackFilter();
            if (packFilterIDs.Contains(mod_xyzzy.AllPacksEnabledID) || packFilterIDs.Contains(packID))
            {
                return true;
            }
            return false;

        }

        /// <summary>
        /// Get a list of enabled pack filters. Returns top 30. 
        /// </summary>
        /// <param name="enabledOnly"></param>
        /// <returns></returns>
        public string getPackFilterStatus()
        {
            //Now build up a message to the user
            string response = "";
            List<Helpers.cardcast_pack> packList = getLocalData().getPackFilterList();
            int recs = 0;
            int extrarecs = 0;
            foreach (Helpers.cardcast_pack pack in packList)
            {
                //is it currently enabled
                if (packEnabled(pack.packID))
                {
                    if (recs < 30) { response += pack.name + "\r\n"; } else { extrarecs++; }
                    recs++;
                } 
            }
            if (extrarecs > 0) { response += ".. plus " + extrarecs + " more."; }
            return response;

        }

        internal void addQuestions()
        {
            // Guards the case packEnabled() never gets called at all (localData.questions is
            // empty) - ensures a chat that's never played still resolves its default pack filter.
            ensureDefaultPackFilter();
            //get a filtered list of q's and a's
            mod_xyzzy_coredata localData = getLocalData();
            List<mod_xyzzy_card> questions = new List<mod_xyzzy_card>();
            foreach (mod_xyzzy_card q in localData.questions)
            {
                if (packEnabled(q.packID)) { questions.Add(q); }
            }
            //limit to 500 question's (then redeal)
            int cardsToAdd = enteredQuestionCount;
            if (cardsToAdd > 500) { cardsToAdd = 500; }
            //pick n questions and put them in the deck
            List<int> cardsPositions =  Helpers.common.getUniquePositions(questions.Count, cardsToAdd);
            foreach (int pos in cardsPositions)
            {
                remainingQuestions.Add(questions[pos].uniqueID);
            }
            log("Added up to " + cardsToAdd + " question cards to the deck, based on a settings of " + enteredQuestionCount + " from a total of " + questions.Count + " choices. There are " + remainingQuestions.Count + " currently in the deck.", logging.loglevel.low);

        }

        public void addAllAnswers()
        {
            ensureDefaultPackFilter(); // see addQuestions()'s own comment
            List<mod_xyzzy_card> answers = new List<mod_xyzzy_card>();
            
            foreach (mod_xyzzy_card a in getLocalData().answers)
            {
                if (packEnabled(a.packID)) { answers.Add(a); }
            }

            //pick n questions and put them in the deck
            List<int> cardsPositions = Helpers.common.getUniquePositions(answers.Count, answers.Count);

            foreach (int pos in cardsPositions)
            {
                remainingAnswers.Add(answers[pos].uniqueID);
            }
            log("Added " + remainingAnswers.Count + " answer cards to the deck, from a total of " + answers.Count + " choices. There are " + remainingAnswers.Count + " currently in the deck.", logging.loglevel.low);
        }
    }
}
