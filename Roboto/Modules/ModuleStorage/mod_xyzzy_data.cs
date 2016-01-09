using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace Roboto.Modules
{
    /// <summary>
    /// CHAT (i.e. game) Data to be stored in the XML store
    /// </summary>
    [XmlType("mod_xyzzy_data")]
    [Serializable]
    public class mod_xyzzy_data : RobotoModuleChatDataTemplate
    {
        public List<String> packFilter = new List<string> { "Base" };
        public enum statusTypes { Stopped, SetGameLength, setPackFilter, cardCastImport, Invites, Question, Judging }
        public statusTypes status = statusTypes.Stopped;
        public List<mod_xyzzy_player> players = new List<mod_xyzzy_player>();
        public int enteredQuestionCount = -1;
        public int lastPlayerAsked = -1; //todo - should be an ID!

        //Store these here, per-chat, so that theres no overlap between chats. Could also help if we want to filter card sets later. Bit heavy on memory, probably. 
        public List<String> remainingQuestions = new List<string>();
        public string currentQuestion;
        public List<String> remainingAnswers = new List<string>();
        //internal mod_xyzzy_data() { }

        /// <summary>
        /// Completely stop the game and clear any player data
        /// </summary>
        public void reset()
        {
            status = statusTypes.Stopped;
            players.Clear();
            remainingAnswers.Clear();
            remainingQuestions.Clear();
            lastPlayerAsked = -1;
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
            mod_xyzzy_player existing = getPlayer(playerID);
            //keep track of the judge!
            mod_xyzzy_player judge = players[lastPlayerAsked];

            if (players.Count == 0)
            {
                log("No players in game", logging.loglevel.high);
                return false;
            }
            else if (existing == null)
            {
                //check everything OK
                log("Couldnt find player to remove, checking consistency", logging.loglevel.warn);
                check();
                return false;
            }  
            else if (judge == null )
            {
                log("Couldn't find judge! ID was" + lastPlayerAsked + ", resetting to 0", logging.loglevel.high);
                lastPlayerAsked = 0;
                judge = players[0];
                //this should really be unneccessary - but had some issues so check anyway
                if (judge == null) { log("Soemthing went really wrong and couldnt set player0 to judge", logging.loglevel.critical); }
            }
            else
            {
                log("Removing " + playerID + ". Current judge is " + judge.playerID + " at pos " + lastPlayerAsked, logging.loglevel.verbose);
            }

            if (existing != null)
            {
                players.Remove(existing);

                //reset the judge ID. Judge should really be populated by this point
                if (judge != null)
                {
                    for (int i = 0; i < players.Count; i++)
                    {
                        if ( players[i] == judge )
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

        internal void askQuestion()
        {
            Roboto.Settings.stats.logStat(new statItem("Hands Played", typeof(mod_xyzzy)));
            mod_xyzzy_coredata localData = getLocalData();
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
                        TelegramAPI.SendMessage(player.playerID, "Its your question! You ask:" + "\n\r" + question.text, false,-1,true);
                    }
                    else
                    {
                        /*int questionMsg = TelegramAPI.GetReply(player.playerID,, -1, true, player.getAnswerKeyboard(localData));*/
                        string questionText = tzar.name + " asks: " + "\n\r" + question.text;
                        //we are expecting a reply to this:
                        TelegramAPI.GetExpectedReply(chatID, player.playerID, questionText, true, typeof(mod_xyzzy), "Question",-1 ,true, player.getAnswerKeyboard(localData));
                    }
                }

                //todo - should this be winner stays on, or round-robbin?
                lastPlayerAsked = playerPos;
                currentQuestion = remainingQuestions[0];
                remainingQuestions.Remove(currentQuestion);
                status = mod_xyzzy_data.statusTypes.Question;
            }
            else
            {
                wrapUp();
            }
        }

        public bool logAnswer(long playerID, string answer)
        {
            mod_xyzzy_coredata localData = getLocalData();
            mod_xyzzy_player player = getPlayer(playerID);
            mod_xyzzy_card question = localData.getQuestionCard(currentQuestion);
            //make sure the response is in the list of cards the player has
            mod_xyzzy_card answerCard = null;
            foreach (string cardUID in player.cardsInHand)
            {
                //find the card, then check the text matches the response.
                mod_xyzzy_card card = localData.getAnswerCard(cardUID);
                if (card != null && card.text == answer)
                {
                    answerCard = card;
                }
            }

            if (answerCard == null)
            {
                log("Couldn't match card against " + answer + " - probably an invalid response");
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
                        TelegramAPI.GetExpectedReply(chatID, player.playerID, "Pick your next card", true, typeof(mod_xyzzy), "Question", -1, true, player.getAnswerKeyboard(localData));
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
            status = statusTypes.Stopped;
            String message = "Game over! You can continue this game with the same players with /xyzzy_extend \n\rScores are: ";
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
                        askQuestion();
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
                status = statusTypes.Judging;
                mod_xyzzy_coredata localData = getLocalData();

                mod_xyzzy_card q = localData.getQuestionCard(currentQuestion);
                mod_xyzzy_player tzar = players[lastPlayerAsked]; // = new mod_xyzzy_player();//


                //get all the responses for the keyboard, and the chat message
                List<string> responses = new List<string>();
                string chatMsg = "All answers recieved! The honourable " + tzar.name + " presiding." + "\n\r" +
                    "Question: " + q.text + "\n\r" + "\n\r";
                string missingRepliestxt = "The following replies were missing: ";
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
        /// Gets the status of the currnet game
        /// </summary>
        public void getStatus()
        {
            
            string response = "The current status of the game is " + status.ToString() + ". " ;
            if (status == mod_xyzzy_data.statusTypes.Stopped)
            {
                response += "Type /xyzzy_start to begin setting up a new game.";

            }
            else
            {
                if (status == statusTypes.Judging || status == statusTypes.Question)
                {
                    response += " with "
                        + remainingQuestions.Count.ToString() + " questions remaining"
                        //                            + chatData.remainingAnswers.Count.ToString() + " answers in the pack. "
                        + ". Say /xyzzy_join to join. The following players are currently playing: \n\r";
                    //order the list of players
                    List<mod_xyzzy_player> orderedPlayers = players.OrderByDescending(e => e.wins).ToList();

                    foreach (mod_xyzzy_player p in orderedPlayers)
                    {
                        response += p.name + " - " + p.wins.ToString() + " points. \n\r";
                    }
                }

                switch (status)
                {
                    case mod_xyzzy_data.statusTypes.Question:
                        response += "The current question is : " + "\n\r" +
                            getLocalData().getQuestionCard(currentQuestion).text + "\n\r" +
                            "The following responses are outstanding :";
                        bool unsentMessages = false;
                        foreach (ExpectedReply r in Roboto.Settings.getExpectedReplies(typeof(mod_xyzzy), chatID, -1, "Question"))
                        {
                            if (r.chatID == chatID)
                            {
                                mod_xyzzy_player p = getPlayer(r.userID);
                                if (p != null)
                                {
                                    response += " " + p.name;
                                    if (p.handle != "") { response += "(@" + p.handle + ")"; }
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

                    case mod_xyzzy_data.statusTypes.Judging:
                        response += "Waiting for " + players[lastPlayerAsked].name + " to judge";
                        break;
                    case statusTypes.cardCastImport:
                    case statusTypes.Invites:
                    case statusTypes.SetGameLength:
                    case statusTypes.setPackFilter:
                        response += "\n\r" + players[0].name + " is currently setting the game up - type /xyzzy_join to join in!";

                        break;
                }
            }

            TelegramAPI.SendMessage(chatID, response, false, -1 , true);
            check();

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
                askQuestion();
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
            mod_xyzzy_coredata localData = getLocalData();
            List<ExpectedReply> replies = Roboto.Settings.getExpectedReplies(typeof(mod_xyzzy), chatID);
            List<ExpectedReply> repliesToRemove = new List<ExpectedReply>();
            log("Status check for " + chatID + ".");

            //is the tzar valid?
            if (lastPlayerAsked >= players.Count)
            {
                lastPlayerAsked = 0;
                log("Reset tzar as ID invalid.");
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
                case statusTypes.Question:
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
                    break;
                case statusTypes.Judging:
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
                    break;
                case statusTypes.Stopped:
                    reset();
                    break;
            }

        }

        public void replaceCard(mod_xyzzy_card old, mod_xyzzy_card newcard, string cardType)
        {
            String actions = "Replacing " + cardType + "card " + old.text + " with " + newcard.text + " in " + getChat().chatID + "." ;
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
            log(actions, logging.loglevel.normal);
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
            foreach (Helpers.cardcast_pack pack in localData.getPackFilterList())
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
            if (enteredQuestionCount > questions.Count || enteredQuestionCount == -1) { enteredQuestionCount = questions.Count; }
            //pick n questions and put them in the deck
            List<int> cardsPositions = mod_xyzzy.getUniquePositions(questions.Count, enteredQuestionCount);
            foreach (int pos in cardsPositions)
            {
                remainingQuestions.Add(questions[pos].uniqueID);
            }

        }

        public void addAllAnswers()
        {
            List<mod_xyzzy_card> answers = new List<mod_xyzzy_card>();
            
            foreach (mod_xyzzy_card a in getLocalData().answers)
            {
                if (packEnabled(a.category)) { answers.Add(a); }
            }
            //TODO - shuffle the list
            foreach (mod_xyzzy_card answer in answers)
            {
                remainingAnswers.Add(answer.uniqueID);
            }
        }
    }
}
