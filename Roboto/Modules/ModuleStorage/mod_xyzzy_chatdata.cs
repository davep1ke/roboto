using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace Roboto.Modules
{

    public enum xyzzy_Statuses { Stopped, SetGameLength, setPackFilter, setMinHours, setMaxHours, cardCastImport, Invites, Question, Judging, waitingForNextHand }

    /// <summary>
    /// CHAT (i.e. game) Data to be stored in the XML store
    /// </summary>
    [XmlType("mod_xyzzy_data")]
    [Serializable]
    public class mod_xyzzy_chatdata : RobotoModuleChatDataTemplate
    {

        //core chat data
        public List<mod_xyzzy_player> players = new List<mod_xyzzy_player>();
        public int lastPlayerAsked = -1; //This is the position in the array, so int is fine! todo - should be an ID!
        public xyzzy_Statuses status = xyzzy_Statuses.Stopped;
        public DateTime statusChangedTime = DateTime.Now;
        public DateTime lastHandStartedTime = DateTime.Now;

        public bool remindersSent = false;

        //chat settings
        public List<String> packFilter = new List<string> { "Base" };
        public int enteredQuestionCount = -1;
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
        }

        //when the status is changed, make a note of the date.
        private void statusChanged()
        {
            statusChangedTime = DateTime.Now;
        }

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
            return Roboto.Settings.getPluginData<mod_xyzzy_coredata>();
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
                TelegramAPI.SendMessage(chatID, "No players in the game to kick");
                return false;
            }
            else if (existing == null)
            {
                //check everything OK
                log("Couldnt find player to remove, checking consistency", logging.loglevel.warn);
                TelegramAPI.SendMessage(chatID, "Couldn't find player to kick");
                check();
                return false;
            }
            else if (players.Count <= 2 && existing != null)
            {
                //removing last player
                TelegramAPI.SendMessage(chatID, "Not enough players to continue, ending game.");
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
                TelegramAPI.SendMessage(chatID, "Removed " + existing.ToString());

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
                        TelegramAPI.SendMessage(chatID, "Judge " + existing.ToString() + " has left, judge is now " + judge.ToString());
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
                List<ExpectedReply> matchedReplies = Roboto.Settings.getExpectedReplies(typeof(mod_xyzzy), chatID, existing.playerID);
                foreach (ExpectedReply exr in matchedReplies)
                {
                    Roboto.Settings.expectedReplies.Remove(exr);
                    log("Removed expectedReply: " + exr.messageData);
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
            Roboto.Settings.stats.logStat(new statItem("Hands Played", typeof(mod_xyzzy)));
            mod_xyzzy_coredata localData = getLocalData();
            //TODO - this causes issues if someone is changing settings in the middle of a round. 
            Roboto.Settings.clearExpectedReplies(chatID, typeof(mod_xyzzy)  ); //shouldnt be needed, but handy if we are forcing a question in debug.

            //check that the question card still exists. 
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

            
            //carry on if force is ticked (e.g. commands from chat for /extend and /question)
            //are we in a quiet period? 
            if (! force && mod_standard.isTimeInQuietPeriod(chatID, DateTime.Now))
            {
                log("About to ask question, but we are in a quiet period. Waiting until awake.", logging.loglevel.warn);
                setStatus(xyzzy_Statuses.waitingForNextHand);

            }
            //do we have a throttle set? If so, are we within the window? 
            if (!force && minWaitTimeHours > 0 && lastHandStartedTime.Add(new TimeSpan(minWaitTimeHours, 0, 0)) > DateTime.Now)
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

                    //loop through each player and act accordingly
                    foreach (mod_xyzzy_player player in players)
                    {
                        //throw away old cards and select new ones. 
                        player.selectedCards.Clear();
                        player.topUpCards(10, remainingAnswers, chatID);
                        if (player == tzar)
                        {
                            TelegramAPI.SendMessage(player.playerID, "Its your question! You ask:" + "\n\r" + question.text, false, -1, true);
                        }
                        else
                        {
                            /*int questionMsg = TelegramAPI.GetReply(player.playerID,, -1, true, player.getAnswerKeyboard(localData));*/
                            string questionText = tzar.name + " asks: " + "\n\r" + question.text;
                            //we are expecting a reply to this:
                            TelegramAPI.GetExpectedReply(chatID, player.playerID, questionText, true, typeof(mod_xyzzy), "Question", -1, true, player.getAnswerKeyboard(localData));
                        }
                    }

                    lastPlayerAsked = playerPos;
                    currentQuestion = remainingQuestions[0];
                    int count = remainingQuestions.Count; 
                    remainingQuestions.Remove(currentQuestion);

                    log("Removing " + question.uniqueID + " from remainingQuestions. Went from " + count.ToString() + " to " + remainingQuestions.Count.ToString() + " remaining.", logging.loglevel.verbose);
                    setStatus(xyzzy_Statuses.Question);
                }
                else
                {
                    //no questions left, finish game
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
                if (card != null && (card.text == answer || Helpers.common.cleanseText(card.text, 100) == Helpers.common.cleanseText(answer, 100)))
                {
                    answerCard = card;
                }
                possibleAnswers += card.text +", ";
            }

            if (answerCard == null)
            {
                log("Couldn't match card against '" + answer 
                    + "' in chat " + chatID
                    + " which has question " + currentQuestion + " (" 
                    + (question.text.Length > 10 ? question.text.Substring(0,10) : question.text )
                    + ") out - probably an invalid response", logging.loglevel.warn );
                log("Possible answers for player " + playerID + " were: " + possibleAnswers, logging.loglevel.verbose);
                //couldnt find answer, reask
                string questionText = players[lastPlayerAsked].name + " asks: " + "\n\r" + question.text;
                //we are expecting a reply to this:
                TelegramAPI.GetExpectedReply(chatID, player.playerID, "Not a valid answer! Try again. " + questionText, true, typeof(mod_xyzzy), "Question", -1, true, player.getAnswerKeyboard(localData));

                //TelegramAPI.SendMessage(playerID, "Not a valid answer! Reply to the original message again, using the keyboard");
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
                    if (player.selectedCards.Count != question.nrAnswers)
                    {
                        TelegramAPI.GetExpectedReply(chatID, player.playerID, "Pick your next card", true, typeof(mod_xyzzy), "Question", -1, true, player.getAnswerKeyboard(localData),false,false,true);
                    }
                }
            }

            //are we ready to start judging? 
            int outstanding = outstandingResponses().Count;
            if (outstanding == 0)
            {
                log("All answers recieved, judging", logging.loglevel.verbose);
                beginJudging();
            }
            else
            {
                string logtxt = "Still waiting for answers. Status is:";
                foreach(mod_xyzzy_player p in players )
                {
                    logtxt += "\n\r" + p.name + " " + p.playerID + " - " + p.selectedCards.Count() + " / " + question.nrAnswers ;
                    foreach (ExpectedReply e in Roboto.Settings.getExpectedReplies(typeof(mod_xyzzy), chatID, p.playerID))
                    {
                        logtxt += " ExpectedReply: " + e.messageData + " sent= " + e.isSent().ToString();
                    }
                }
                log(logtxt, logging.loglevel.verbose);
            }


            return true;
        }


        /// <summary>
        /// Calculate which players still need to responsd.
        /// </summary>
        /// <returns></returns>
        private List<mod_xyzzy_player> outstandingResponses()
        {
            List<mod_xyzzy_player> players = new List<mod_xyzzy_player>();

            List<ExpectedReply> expectedReplies = Roboto.Settings.getExpectedReplies(typeof(mod_xyzzy), chatID, -1, "Question");

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
            Roboto.Settings.clearExpectedReplies(chatID, typeof(mod_xyzzy));
            setStatus(xyzzy_Statuses.Stopped);
            String message = "Game over!";
            if (players.Count > 1) { message += " You can continue this game with the same players with /xyzzy_extend"; }
            message += "\n\rScores are: ";
            foreach (mod_xyzzy_player p in players.OrderByDescending(x => x.wins))
            {
                message += "\n\r" + p.name + " - " + p.wins.ToString() + " points";
            }

            TelegramAPI.SendMessage(chatID, message);
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

        internal void beginJudging(bool judgesMessageOnly = false)
        {

            if (players.Count == 0)
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
                mod_xyzzy_player tzar = players[lastPlayerAsked]; // = new mod_xyzzy_player();//


                //get all the responses for the keyboard, and the chat message
                List<string> responses = new List<string>();
                string chatMsg = "All answers recieved! The honourable " + tzar.name + " presiding." + "\n\r" +
                    "Question: " + q.text + "\n\r" + "\n\r";
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
                            mod_xyzzy_card card = localData.getAnswerCard(cardUID);
                            if (answer != "") { answer += " >> "; }
                            answer += card.text;
                        }
                        if (answer != "")
                        {
                            responses.Add(answer);
                        }
                        else
                        {
                            missingReplies = true;
                            missingRepliestxt += "\n\r" + " - " + p.name;
                        }
                    }
                }
                responses.Sort(); //sort so that player order isnt same each time.

                foreach (string answer in responses) { chatMsg += "  - " + answer + "\n\r"; }

                if (missingReplies) { chatMsg += missingRepliestxt; }

                string keyboard = TelegramAPI.createKeyboard(responses, 1);
                //int judgeMsg = TelegramAPI.GetReply(tzar.playerID, "Pick the best answer! \n\r" + q.text, -1, true, keyboard);
                //localData.expectedReplies.Add(new mod_xyzzy_expectedReply(judgeMsg, tzar.playerID, chatID, ""));
                //TODO - add messageData types to an enum
                TelegramAPI.GetExpectedReply(chatID, tzar.playerID, "Pick the best answer! \n\r" + q.text, true, typeof(mod_xyzzy), "Judging", -1, true, keyboard);

                //Send the general chat message
                if (!judgesMessageOnly)
                {
                    TelegramAPI.SendMessage(chatID, chatMsg);
                }
            }
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
                response += "Shhh... its sleepy times" + "\n\r";
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
                response += " Type /xyzzy_start to begin setting up a new game.";

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

                    response += " Say /xyzzy_join to join.";
                    response += " The following players are currently playing: \n\r";
                    //order the list of players
                    List<mod_xyzzy_player> orderedPlayers = players.OrderByDescending(e => e.wins).ToList();

                    foreach (mod_xyzzy_player p in orderedPlayers)
                    {
                        response += p.name + " - " + p.wins.ToString() + " points. \n\r";
                    }
                }

                switch (status)
                {
                    case xyzzy_Statuses.Question:
                        response += "The current question is : " + "\n\r" +
                            getLocalData().getQuestionCard(currentQuestion).text + "\n\r" +
                            "Still waiting on the following players :";
                        bool unsentMessages = false;
                        bool first = true;
                        foreach (ExpectedReply r in Roboto.Settings.getExpectedReplies(typeof(mod_xyzzy), chatID, -1, "Question"))
                        {
                            if (r.chatID == chatID)
                            {
                                mod_xyzzy_player p = getPlayer(r.userID);
                                if (p != null)
                                {
                                    response += (first? " " : ", ") + p.ToString();
                                    if (first) { first = false; }
                                    if (!r.isSent())
                                    {
                                        response += "(*)";
                                        unsentMessages = true;
                                    }
                                    
                                }
                            }
                        }
                        if (unsentMessages) { response += "\n\r" + "(*) These messages have not yet been sent, as I am waiting for a reply to another question!"; }

                        break;

                    case xyzzy_Statuses.Judging:
                        response += "Waiting for " + players[lastPlayerAsked].ToString() +  " to judge";
                        break;
                    case xyzzy_Statuses.cardCastImport:
                    case xyzzy_Statuses.Invites:
                    case xyzzy_Statuses.SetGameLength:
                    case xyzzy_Statuses.setPackFilter:
                    case xyzzy_Statuses.setMaxHours:
                    case xyzzy_Statuses.setMinHours:
                        response += "\n\r" + players[0].name + " is currently setting the game up - type /xyzzy_join to join in!";

                        break;
                }
            }

            TelegramAPI.SendMessage(chatID, response, false, -1 , true);
            check();

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
            string kb = TelegramAPI.createKeyboard(new List<string>()
            {
                "Continue", "No Timeout"
                , "1","2", "6", "12", "24", "48"
            }, 2);

            TelegramAPI.GetExpectedReply(chatID, userID, "Do you want to set a timeout? Enter how long (in hours) before someone is skipped, or 'Continue' to accept the last value ("
                + (maxWaitTimeHours == 0 ? "No Timeout" : maxWaitTimeHours.ToString()) + ")"
                , true, typeof(mod_xyzzy), "setMaxHours", -1, true, kb );
        }

        /// <summary>
        /// Ask the user what the throttle should be set to
        /// </summary>
        /// <param name="userID"></param>
        public void askMinTimeout(long userID)
        {

            string kb = TelegramAPI.createKeyboard(new List<string>()
            {
                "Continue"
                , "1","2", "6", "12", "24", "48"
            }, 2);

            TelegramAPI.GetExpectedReply(chatID, userID, "Do you want to set a throttle? Enter how long (in hours) before a new round can start, or 'Continue' to accept the last value ("
                + (minWaitTimeHours == 0 ? "No Throttle" : minWaitTimeHours.ToString()) + ")"
                , true, typeof(mod_xyzzy), "setMinHours", -1, true, kb);
        }

        /// <summary>
        /// Set the timeout value
        /// </summary>
        /// <param name="text_msg"></param>
        /// <returns></returns>
        public bool setMinTimeout(string text_msg)
        {
            if (text_msg == "Continue") { return true; }
            else if (text_msg == "No Throttle")
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
            foreach (mod_xyzzy_player p in players)
            {
                //handle multiple answers for a question 
                string answer = "";
                foreach (string cardUID in p.selectedCards)
                {
                    mod_xyzzy_card card = localData.getAnswerCard(cardUID);
                    if (answer != "") { answer += " >> "; }
                    answer += card.text;
                }

                //Keyboard seems to trim the answers at about 110 chars, so ignore anything after that point. 
                if (answer.Substring(0, Math.Min(answer.Length, 100)) == chosenAnswer.Substring(0, Math.Min(chosenAnswer.Length, 100)))
                //if (answer == chosenAnswer)
                    {
                    winner = p;
                }
            }

            if (winner != null)
            {
                //give the winning player a point. 
                winner.wins++;
                string message = winner.name + " wins a point!\n\rQuestion: " + q.text + "\n\rAnswer:" + chosenAnswer + "\n\rThere are " + remainingQuestions.Count.ToString() + " questions remaining. Current scores are: ";
                List<mod_xyzzy_player> orderedPlayers = players.OrderByDescending(e => e.wins).ToList();
                foreach (mod_xyzzy_player p in orderedPlayers)
                {
                    message += "\n\r" + p.name + " - " + p.wins.ToString() + " points";
                }

                TelegramAPI.SendMessage(chatID, message);

                //ask the next question (will jump to summary if no more questions). 
                askQuestion(false);
            }
            else
            {
                //answer not found?
                TelegramAPI.SendMessage(tzar.playerID, "Couldnt find your answer, try again?");
                beginJudging(true);
                return false;
            }
            return true;
        }


        /// <summary>
        /// Check consistency of game state
        /// </summary>
        internal void check()
        {
            log("Performing status check for " + chatID + ".", logging.loglevel.low);

            mod_xyzzy_coredata localData = getLocalData();
            List<ExpectedReply> replies = Roboto.Settings.getExpectedReplies(typeof(mod_xyzzy), chatID);
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
                TelegramAPI.SendMessage(chatID, "Stopping game, not enough players");
                wrapUp();
            }

            //responses from non-existent players
            foreach (ExpectedReply reply in Roboto.Settings.getExpectedReplies(typeof(mod_xyzzy), chatID ))
            {
                if (reply.chatID == chatID)
                {
                    if (getPlayer(reply.userID) == null)
                    {
                        repliesToRemove.Add(reply);
                    }
                }
            }
            foreach (ExpectedReply r in repliesToRemove)
            {
                Roboto.Settings.removeReply(r);
                log(" Removed expected reply " + r.userID + "\\" + r.pluginType.ToString() + "\\" + r.messageData + ".");
            }

            //todo - Remove non-existant cards anywhere (e.g. if a sync has recently happened)


            //do we have any duplicate cards? rebuild the list
            int count_q = remainingQuestions.Count;
            int count_a = remainingAnswers.Count;
            remainingQuestions = remainingQuestions.Distinct().ToList();
            remainingAnswers = remainingAnswers.Distinct().ToList();

            //duplicate strings in packfilter?
            packFilter = packFilter.Distinct().ToList();

            //current status
            //TODO - pad this out more
            //TODO - call regularly. 
            //TODO - does the tzar exist?
            //TODO - check when removing the tzar
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
                        TelegramAPI.SendMessage(chatID, outstandingPlayers + " can suck it for not answering in time:");

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
                            { TelegramAPI.SendMessage(chatID, outstandingPlayers + " needs to hurry up! Tick-tock motherfucker..."); }
                            else
                            { TelegramAPI.SendMessage(chatID, outstandingPlayers + " need to hurry up! Tick-tock motherfuckers..."); }
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
                        Roboto.Settings.clearExpectedReplies(chatID, typeof(mod_xyzzy));
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
                        Roboto.Settings.clearExpectedReplies(chatID, typeof(mod_xyzzy));
                        reask = true;
                        log("Removed invalid expected reply during judging from game " + chatID.ToString(), logging.loglevel.high);
                    }

                    if (reask)
                    {
                        beginJudging(true);
                        log("Redid judging", logging.loglevel.critical);
                    }
                    else
                    {

                        //send reminder to judge
                        if (maxWaitTimeHours > 0 && remindersSent == false && DateTime.Now > reminderTime)
                        {
                            log("Sending judge reminder for chat " + chatID);

                            TelegramAPI.SendMessage(chatID, "Hurry up Judgy Judgerson! (" + players[lastPlayerAsked].ToString() + ")");
                            remindersSent = true;
                        }
                        //abandon judging, too slow
                        else if (maxWaitTimeHours > 0 && DateTime.Now > abandonTime)
                        {
                            log("Skipping judging for chat" + chatID);

                            TelegramAPI.SendMessage(chatID, "Judge was too slow, " + players[lastPlayerAsked].ToString() + " gets docked a point!");
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
                    
                case xyzzy_Statuses.Stopped:
                    reset();
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
        internal void setPackFilter(message m)
        {
            setPackFilter(m, m.text_msg);
        }

        /// <summary>
        /// Ask the organiser which packs they want to play with. Option to continue, or to select all packs. 
        /// </summary>
        /// <param name="m"></param>
        /// <param name="packName"></param>
        internal void setPackFilter (message m, string packName )
        { 
            mod_xyzzy_coredata localData = getLocalData();

            //did they actually give us an answer? 
            if (m.text_msg == "All")
            {
                packFilter.Clear();

                foreach(Helpers.cardcast_pack pack in localData.getPackFilterList())
                {
                    packFilter.Add(pack.name);
                }

                //packFilter.AddRange(localData.getPackFilterList());
            }
            else if (m.text_msg == "None")
            {
                packFilter.Clear();
            }
            else if (localData.getPackFilterList().Where(x => x.name == packName).Count() == 0 )//      .Contains(packName))
            {
                TelegramAPI.SendMessage(m.chatID, "Not a valid pack!", false, m.message_id);
            }
            else
            {
                //toggle the pack
                if (packFilter.Contains(packName))
                {
                    packFilter.Remove(packName);
                }
                else
                {
                    packFilter.Add(packName);
                }
            }

        }

        public void sendPackFilterMessage(message m)
        {
            mod_xyzzy_coredata localData = getLocalData();
            String response = "The following packs are available, and their current status is as follows:" + "\n\r" + getPackFilterStatus() +
             "You can toggle the packs using the keyboard below, or click 'Continue' to start the game. You can import packs from CardCast" +
             "by clicking 'Import CardCast Pack'";


            //Now build up keybaord
            List<String> keyboardResponse = new List<string> { "Continue", "Import CardCast Pack", "All", "None" };
            foreach (Helpers.cardcast_pack pack in localData.getPackFilterList().Take(50).OrderBy(x => x.name).ToList()) //TODO - replace this with some kind of paging mechanism.
            {
                keyboardResponse.Add(pack.name);
            }

            //now send the new list. 
            string keyboard = TelegramAPI.createKeyboard(keyboardResponse, 2);//todo columns
            TelegramAPI.GetExpectedReply(chatID, m.userID, response, true, typeof(mod_xyzzy), "setPackFilter", -1, false, keyboard);
        }


        public bool packEnabled(string packName)
        {
            if (packFilter.Contains("*") || packFilter.Contains(packName.Trim()))
            {
                return true;
            }
            return false;

        }

        internal string getPackFilterStatus()
        {
            //Now build up a message to the user
            string response = "";
            mod_xyzzy_coredata localData = getLocalData();
            foreach (Helpers.cardcast_pack pack in localData.getPackFilterList())
            {
                //is it currently enabled
                if (packEnabled(pack.name))
                {
                    response += "ON  ";
                }
                else
                {
                    response += "OFF ";
                }
                response += pack.name + "\n\r";
            }
            return response;

        }

        internal void addQuestions()
        {
            //get a filtered list of q's and a's
            mod_xyzzy_coredata localData = getLocalData();
            List<mod_xyzzy_card> questions = new List<mod_xyzzy_card>();
            foreach (mod_xyzzy_card q in localData.questions)
            {
                if (packEnabled(q.category)) { questions.Add(q); }
            }
            //limit to 500 question's (then redeal)
            if (enteredQuestionCount > 500) { enteredQuestionCount = 500; }
            //pick n questions and put them in the deck
            List<int> cardsPositions =  Helpers.common.getUniquePositions(questions.Count, enteredQuestionCount);
            foreach (int pos in cardsPositions)
            {
                remainingQuestions.Add(questions[pos].uniqueID);
            }
            log("Added up to " + enteredQuestionCount + " question cards to the deck, from a total of " + questions.Count + " choices. There are " + remainingQuestions.Count + " currently in the deck.", logging.loglevel.low);

        }

        public void addAllAnswers()
        {
            List<mod_xyzzy_card> answers = new List<mod_xyzzy_card>();
            
            foreach (mod_xyzzy_card a in getLocalData().answers)
            {
                if (packEnabled(a.category)) { answers.Add(a); }
            }

            //pick n questions and put them in the deck
            List<int> cardsPositions = Helpers.common.getUniquePositions(answers.Count, answers.Count);

            foreach (int pos in cardsPositions)
            {
                remainingAnswers.Add(answers[pos].uniqueID);
            }
            log("Added answer cards to the deck, from a total of " + answers.Count + " choices. There are " + remainingAnswers.Count + " currently in the deck.", logging.loglevel.low);
        }
    }
}
