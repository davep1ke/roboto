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
        public enum statusTypes { Stopped, SetGameLength, setPackFilter, Invites, Question, Judging }
        public statusTypes status = statusTypes.Stopped;
        public List<mod_xyzzy_player> players = new List<mod_xyzzy_player>();
        public int enteredQuestionCount = -1;
        public int lastPlayerAsked = -1; //todo - should be an ID!

        //Store these here, per-chat, so that theres no overlap between chats. Could also help if we want to filter card sets later. Bit heavy on memory, probably. 
        public List<String> remainingQuestions = new List<string>();
        public string currentQuestion;
        public List<String> remainingAnswers = new List<string>();
        //internal mod_xyzzy_data() { }


        public void reset()
        {
            status = statusTypes.Stopped;
            players.Clear();
            remainingAnswers.Clear();
            remainingQuestions.Clear();
            lastPlayerAsked = -1;
        }

        public mod_xyzzy_player getPlayer(int playerID)
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

        internal bool removePlayer(int playerID)
        {
            mod_xyzzy_player existing = getPlayer(playerID);
            //keep track of the judge!
            mod_xyzzy_player judge = getPlayer(lastPlayerAsked);
            //TODO - what if this is the judge that we are removing?
            if (existing != null)
            {
                players.Remove(existing);
                return true;
            }

            //reset the judge ID
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] == judge) { lastPlayerAsked = i; }
            }

            return false;
        }

        internal void askQuestion()
        {
            mod_xyzzy_coredata localData = getLocalData();
            Roboto.Settings.clearExpectedReplies(chatID, typeof(mod_xyzzy)  ); //shouldnt be needed, but handy if we are forcing a question in debug.

            if (remainingQuestions.Count > 0)
            {

                mod_xyzzy_card question = localData.getQuestionCard(remainingQuestions[0]);
                int playerPos = lastPlayerAsked + 1;
                if (playerPos >= players.Count) { playerPos = 0; }
                mod_xyzzy_player tzar = players[playerPos];

                //loop through each player and act accordingly
                foreach (mod_xyzzy_player player in players)
                {
                    //throw away old cards and select new ones. 
                    player.selectedCards.Clear();
                    player.topUpCards(10, remainingAnswers);
                    if (player == tzar)
                    {
                        TelegramAPI.SendMessage(player.playerID, "Its your question! You ask:" + "\n\r" + question.text);
                    }
                    else
                    {
                        /*int questionMsg = TelegramAPI.GetReply(player.playerID,, -1, true, player.getAnswerKeyboard(localData));*/
                        string questionText = tzar.name + " asks: " + "\n\r" + question.text;
                        //we are expecting a reply to this:
                        TelegramAPI.GetExpectedReply(chatID, player.playerID, questionText, true, typeof(mod_xyzzy), "question",-1 ,true, player.getAnswerKeyboard(localData));
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

        public bool logAnswer(int playerID, string answer)
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
                TelegramAPI.SendMessage(playerID, "Not a valid answer! Reply to the original message again, using the keyboard");
                return false;
            }
            else
            {
                //valid response, remove the card from the deck, and add it to our list of responses
                bool success = player.SelectAnswerCard(answerCard.uniqueID);
                if (!success)
                {
                    throw new ArgumentOutOfRangeException ("Card couldnt be selected for some reason!");
                }
                else
                {
                    //just check if this needs more responses:
                    if (player.selectedCards.Count != question.nrAnswers)
                    {
                        TelegramAPI.GetExpectedReply(chatID, player.playerID, "Pick your next card", true, typeof(mod_xyzzy), "question", -1, true, player.getAnswerKeyboard(localData));
                    }
                }
            }

            //are we ready to start judging? 
            if (outstandingResponses().Count == 0)
            {
                beginJudging();
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

            List<ExpectedReply> expectedReplies = Roboto.Settings.getExpectedReplies(typeof(mod_xyzzy), chatID, -1, "question");

            foreach (ExpectedReply r in expectedReplies)
            {
                mod_xyzzy_player player = getPlayer(r.userID);
                players.Add(player);
            }
            return players;
        }

        private void wrapUp()
        {
            status = statusTypes.Stopped;
            String message = "Game over! You can continue this game with the same players with /xyzzy_extend \n\rScores are: ";
            foreach (mod_xyzzy_player p in players)
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
                    if (p.selectedCards.Count < getLocalData().getQuestionCard(currentQuestion).nrAnswers) { return false; }
                }
            }
            return true;

        }

        internal void beginJudging()
        {

            status = statusTypes.Judging;
            mod_xyzzy_coredata localData = getLocalData();

            mod_xyzzy_card q = localData.getQuestionCard(currentQuestion);
            mod_xyzzy_player tzar = players[lastPlayerAsked];
            //get all the responses for the keyboard, and the chat message
            List<string> responses = new List<string>();
            string chatMsg = "All answers recieved! The honourable " + tzar.name + " presiding." + "\n\r" +
                "Question: " + q.text + "\n\r" + "\n\r";
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
                    responses.Add(answer);

                }
            }
            responses.Sort(); //sort so that player order isnt same each time.

            foreach (string answer in responses) { chatMsg += "  - " + answer + "\n\r"; }
            
            string keyboard = TelegramAPI.createKeyboard(responses, 1);
            //int judgeMsg = TelegramAPI.GetReply(tzar.playerID, "Pick the best answer! \n\r" + q.text, -1, true, keyboard);
            //localData.expectedReplies.Add(new mod_xyzzy_expectedReply(judgeMsg, tzar.playerID, chatID, ""));
            //TODO - add messageData types to an enum
            TelegramAPI.GetExpectedReply(chatID, tzar.playerID, "Pick the best answer! \n\r" + q.text, true, typeof(mod_xyzzy), "judging", -1, true, keyboard);

            //Send the general chat message
            TelegramAPI.SendMessage(chatID, chatMsg);

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

                if (answer == chosenAnswer)
                {
                    winner = p;
                }
            }

            if (winner != null)
            {
                //give the winning player a point. 
                winner.wins++;
                string message = winner.name + " wins a point!\n\rQuestion: " + q.text + "\n\rAnswer:" + chosenAnswer + "\n\rThere are " + remainingQuestions.Count.ToString() + " questions remaining. Current scores are: ";
                foreach (mod_xyzzy_player p in players)
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
                //TODO - needs to be another expected reply
                TelegramAPI.SendMessage(tzar.playerID, "Couldnt find your answer, try again?");
                return false;
            }
            return true;
        }


        /// <summary>
        /// Check consistenct of game state
        /// </summary>
        internal void check()
        {
            mod_xyzzy_coredata localData = getLocalData();
            List<ExpectedReply> repliesToRemove = new List<ExpectedReply>();

            //is the tzar valid?
            if (lastPlayerAsked >= players.Count) { lastPlayerAsked = 0; }

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
            }

            //do we have any duplicate cards? rebuild the list
            int count_q = remainingQuestions.Count;
            int count_a = remainingAnswers.Count;
            remainingQuestions = remainingQuestions.Distinct().ToList();
            remainingAnswers = remainingAnswers.Distinct().ToList();

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
                    break;
            }

        }

        /// <summary>
        /// Ask the organiser which packs they want to play with. Option to continue, or to select all packs. 
        /// </summary>
        /// <param name="m"></param>
        internal void setPackFilter(message m)
        {
            mod_xyzzy_coredata localData = getLocalData();

            //did they actually give us an answer? 
            if (m.text_msg == "All")
            {
                packFilter.Clear();
                packFilter.AddRange(localData.getPackFilterList());
            }
            else if (m.text_msg == "None")
            {
                packFilter.Clear();
            }
            else if (!localData.getPackFilterList().Contains(m.text_msg))
            {
                TelegramAPI.SendMessage(m.chatID, "Not a valid pack!", false, m.message_id);
            }
            else
            {
                //toggle the pack
                if (packFilter.Contains(m.text_msg))
                {
                    packFilter.Remove(m.text_msg);
                }
                else
                {
                    packFilter.Add(m.text_msg);
                }
            }

        }

        public void sendPackFilterMessage(message m)
        {
            mod_xyzzy_coredata localData = getLocalData();
            String response = "The following packs are available, and their current status is as follows:" + "\n\r" + getPackFilterStatus() +
             "You can toggle the messages using the keyboard below, or click Continue to start the game";


            //Now build up keybaord
            List<String> keyboardResponse = new List<string> { "Continue", "All", "None" };
            foreach (string packName in localData.getPackFilterList())
            {
                keyboardResponse.Add(packName);
            }

            //now send the new list. 
            string keyboard = TelegramAPI.createKeyboard(keyboardResponse, 3);//todo columns
            TelegramAPI.GetExpectedReply(chatID, m.userID, response, true, typeof(mod_xyzzy), "setPackFilter", -1, true, keyboard);
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
            foreach (string packName in localData.getPackFilterList())
            {
                //is it currently enabled
                if (packEnabled(packName))
                {
                    response += "ON  ";
                }
                else
                {
                    response += "OFF ";
                }
                response += packName + "\n\r";
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
    }
}
