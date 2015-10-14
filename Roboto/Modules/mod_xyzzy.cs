using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace Roboto.Modules
{

    /// <summary>
    /// General Data to be stored in the plugin XML store.
    /// </summary>
    [XmlType("mod_xyzzy_coredata")]
    [Serializable]
    public class mod_xyzzy_coredata : RobotoModuleDataTemplate
    {
        public DateTime lastDayProcessed = DateTime.MinValue;
        public List<mod_xyzzy_card> questions = new List<mod_xyzzy_card>();
        public List<mod_xyzzy_card> answers = new List<mod_xyzzy_card>();
        public List<mod_xyzzy_expectedReply> expectedReplies = new List<mod_xyzzy_expectedReply>(); //replies expected by the various chats
        //internal mod_xyzzy_coredata() { }

        public void clearExpectedReplies(int chat_id)
        {
            //find replies for this chat, and add them to a temp list
            List<mod_xyzzy_expectedReply> repliesToRemove = new List<mod_xyzzy_expectedReply>();
            foreach (mod_xyzzy_expectedReply reply in expectedReplies)
            {
                if (reply.chatID == chat_id) { repliesToRemove.Add(reply); }
            }
            //now remove them
            foreach (mod_xyzzy_expectedReply reply in repliesToRemove)
            {
                expectedReplies.Remove(reply);
            }
        }

        public mod_xyzzy_card getQuestionCard(string cardUID)
        {
            foreach (mod_xyzzy_card c in questions)
            {
                if (c.uniqueID == cardUID) { return c; }
            }
            return null;
        }

        public mod_xyzzy_card getAnswerCard(string cardUID)
        {
            foreach (mod_xyzzy_card c in answers)
            {
                if (c.uniqueID == cardUID) 
                { 
                    return c; 
                }
            }
            return null;
        }

        public List<String> getPackFilterList()
        {
            //include "all"
            List<String> packs = new List<string>();
            foreach (mod_xyzzy_card q in questions)
            {
                packs.Add(q.category.Trim());
            }
            foreach (mod_xyzzy_card a in answers)
            {
                packs.Add(a.category.Trim());
            }
            return packs.Distinct().ToList();
        }

    }

    /// <summary>
    /// CHAT (i.e. game) Data to be stored in the XML store
    /// </summary>
    [XmlType("mod_xyzzy_data")]
    [Serializable]
    public class mod_xyzzy_data : RobotoModuleChatDataTemplate
    {
        public List<String> packFilter = new List<string>{"Base"};
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
            localData.clearExpectedReplies(chatID); //shouldnt be needed, but handy if we are forcing a question in debug.

            if (remainingQuestions.Count > 0)
            {

                mod_xyzzy_card question = localData.getQuestionCard(remainingQuestions[0]);
                int playerPos = lastPlayerAsked+1;
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
                        int questionMsg = TelegramAPI.GetReply(player.playerID, tzar.name + " asks: "
                            + "\n\r" + question.text, -1, true, player.getAnswerKeyboard(localData));
                        //we are expecting a reply to this:
                        localData.expectedReplies.Add(new mod_xyzzy_expectedReply(questionMsg, player.playerID, chatID, ""));
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
                //valid response, remove the card from the deck, and add it to our list of 
                bool success = player.SelectAnswerCard(answerCard.uniqueID);
                if (!success)
                {
                    throw new InvalidDataException("Card couldnt be selected for some reason!");
                }
                else
                {
                    //just check if this needs more responses:
                    if (player.selectedCards.Count == question.nrAnswers)
                    {
                        //remove the expected reply. 
                        mod_xyzzy_expectedReply playerReply = null;
                        foreach (mod_xyzzy_expectedReply reply in localData.expectedReplies)
                        {
                            if (reply.chatID == chatID && reply.playerID == playerID)
                            {
                                playerReply = reply;
                            }
                        }
                        if (playerReply != null) 
                        { 
                            localData.expectedReplies.Remove(playerReply); 
                        }
                    }
                    else
                    {
                        //more responses needed
                        int questionMsg = TelegramAPI.GetReply(player.playerID, "Pick your next card", -1, true, player.getAnswerKeyboard(localData));
                        //dont need to add expected reply, one already there. 
                    }
                }
            }

            //have all responses been recieved? 
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
            
            foreach (mod_xyzzy_expectedReply r in getLocalData().expectedReplies)
            {
                if (r.chatID == chatID)
                {
                    mod_xyzzy_player player = getPlayer(r.playerID);
                    players.Add(player);
                }
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
            

            string keyboard = TelegramAPI.createKeyboard(responses,1);
            int judgeMsg = TelegramAPI.GetReply(tzar.playerID, "Pick the best answer! \n\r" + q.text, -1, true, keyboard);
            localData.expectedReplies.Add(new mod_xyzzy_expectedReply(judgeMsg, tzar.playerID, chatID, ""));

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
                string message = winner.name + " wins a point!\n\rQuestion: " + q.text+ "\n\rAnswer:" + chosenAnswer + "\n\rThere are " + remainingQuestions.Count.ToString() + " questions remaining. Current scores are: ";
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
            List<mod_xyzzy_expectedReply> repliesToRemove = new List<mod_xyzzy_expectedReply>();

            //is the tzar valid?
            if (lastPlayerAsked >= players.Count) { lastPlayerAsked = 0; }

            //responses from non-existent players
            foreach (mod_xyzzy_expectedReply reply in localData.expectedReplies)
            {
                if (reply.chatID == chatID)
                {
                    if (getPlayer(reply.playerID) == null)
                    {
                        repliesToRemove.Add(reply);
                    }
                }
            }
            foreach (mod_xyzzy_expectedReply r in repliesToRemove)
            {
                localData.expectedReplies.Remove(r);
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
                case statusTypes.Question :
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
            string keyboard = TelegramAPI.createKeyboard(keyboardResponse,3);//todo columns
            TelegramAPI.GetReply(m.userID, response, -1, true, keyboard);
            //NB: Should already be an expected response here, as we havent cleared it from last time. 
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

    /// <summary>
    /// Represents a reply we are expecting
    /// TODO - maybe genericise this?
    /// </summary>
    public class mod_xyzzy_expectedReply
    {
        public int messageID;
        public int chatID;
        public int playerID;
        public string replyData; //somewhere to store stuff about the reply
        internal mod_xyzzy_expectedReply() { }
        public mod_xyzzy_expectedReply(int messageID, int playerID, int chatID, string replyData)
        {
            this.messageID = messageID;
            this.playerID = playerID;
            this.chatID = chatID;
            this.replyData = replyData;
        }
    }


    /// <summary>
    /// Represents a xyzzy player
    /// </summary>
    public class mod_xyzzy_player
    {
        public string name;
        public int playerID;
        public int wins = 0;
        public List<String> cardsInHand = new List<string>();
        public List<String> selectedCards = new List<string>();
        internal mod_xyzzy_player() { }
        public mod_xyzzy_player(string name, int playerID)
        {
            this.name = name;
            this.playerID = playerID;
        }

        internal void topUpCards(int nrCards, List<string> availableAnswers)
        {
            while (cardsInHand.Count < nrCards)
            {
                //pick a card
                //TODO - what if answers is empty? 
                string cardUID = availableAnswers[settings.getRandom(availableAnswers.Count)];
                cardsInHand.Add(cardUID);

                //remove it from the available list
                availableAnswers.Remove(cardUID);
            }
        }

        public bool SelectAnswerCard(string cardUID)
        {
            bool success = cardsInHand.Remove(cardUID);
            if (success)
            {
                selectedCards.Add(cardUID);
            }
            return success;

        }

        public string getAnswerKeyboard(mod_xyzzy_coredata localData)
        {
            List<string> answers = new List<string>();

            foreach (string cardID in cardsInHand)
            {
                mod_xyzzy_card c = localData.getAnswerCard(cardID);
                answers.Add(c.text);
            }
            return (TelegramAPI.createKeyboard(answers,1));
         }


        
   }


    /// <summary>
    /// Represents a xyzzy card
    /// </summary>
    public class mod_xyzzy_card
    {
        public string uniqueID = Guid.NewGuid().ToString();
        public String text;
        public String category; //what pack did the card come from
        public int nrAnswers = -1; 

        internal mod_xyzzy_card() { }
        public mod_xyzzy_card(String text, string category, int nrAnswers = -1)
        {
            this.text = text;
            this.category = category;
            this.nrAnswers = nrAnswers;
        }

    }


    /// <summary>
    /// The XXZZY Plugin
    /// </summary>
    public class mod_xyzzy : RobotoModuleTemplate
    {
        private mod_xyzzy_coredata localData;

        public override void init()
        {
            pluginDataType = typeof(mod_xyzzy_coredata);
            pluginChatDataType = typeof(mod_xyzzy_data);

            chatHook = true;
            chatEvenIfAlreadyMatched = false;
            chatPriority = 3;

            //backgroundHook = true;
            //backgroundMins = 1;

        }

        public override string getMethodDescriptions()
        {
            return
                "xyzzy_start - Starts a game of xyzzy with the players in the chat" + "\n\r" +
                "xyzzy_join - Join a game of xyzzy that is in progress, or about to start" + "\n\r" +
                "xyzzy_leave - Join a game of xyzzy that is in progress, or about to start" + "\n\r" +
                "xyzzy_extend - Extends a running game with more cards, or restarts a game that has just stopped" + "\n\r" +
                "xyzzy_abandon - Abandons the game" + "\n\r" +
                "xyzzy_kick - Kicks a player from a game" + "\n\r" +
                "xyzzy_status - Gets the current status of the game" + "\n\r" +
                "xyzzy_filter - Shows the filters and their current status";
        }

        public override void initData()
        {
            try
            {
                localData = Roboto.Settings.getPluginData<mod_xyzzy_coredata>();
            }
            catch (InvalidDataException)
            {
                //Data doesnt exist, create, populate with sample data and register for saving
                localData = new mod_xyzzy_coredata();
                sampleData();
                Roboto.Settings.registerData(localData);
            }

            Console.WriteLine(localData.questions.Count.ToString() + " questions and " + localData.answers.Count.ToString() + " answers loaded for xyzzy");

        }

        public override void initChatData(chat c)
        {
            mod_xyzzy_data chatData = c.getPluginData<mod_xyzzy_data>();
           
            if (chatData == null)
            {
                //Data doesnt exist, create, populate with sample data and register for saving
                chatData = new mod_xyzzy_data();
                c.addChatData(chatData);
            }
        }

        /// <summary>
        /// Process chat messages
        /// </summary>
        /// <param name="m"></param>
        /// <param name="c"></param>
        /// <returns></returns>
        public override bool chatEvent(message m, chat c = null)
        {
            //Various bits of setup before starting to process the message
            bool processed = false;
            mod_xyzzy_expectedReply expectedInReplyTo = null;
            if (m.text_msg == "bip")
            {
                int i = 1;
            }

            if (c == null && m.isReply)
            {
                //any non-chat messages should only be in reply to messages that we have logged. Find the chatID of the message, and get the chat object
                foreach (mod_xyzzy_expectedReply testReply in localData.expectedReplies)
                {
                    if (testReply.messageID == m.replyMessageID)
                    {
                        //get the chat & reply objects.
                        expectedInReplyTo = testReply;
                        c = Roboto.Settings.getChat(testReply.chatID);
                    }
                }
            }

          if (c == null)
            {
                //except for this stupid thing, where if you reply to a message in a private chat, it doesnt give the original ID. So guess if it is or not. 
                //TODO - will fuck up playuing in multiple games. 
                if (expectedInReplyTo == null)
                {
                    foreach (mod_xyzzy_expectedReply testReply in localData.expectedReplies)
                    {
                        if (testReply.playerID == m.userID)
                        {
                            //get the chat & reply objects.
                            expectedInReplyTo = testReply;
                            c = Roboto.Settings.getChat(testReply.chatID);
                        }
                    }
                }
            }



            if (c != null) //Setup needs to be done in a chat! Other replies will now have a chat object passed in here too!
            {
                //get current game data. 
                mod_xyzzy_data chatData = c.getPluginData<mod_xyzzy_data>();


                if (m.isReply && m.replyOrigMessage == "Which player do you want to kick" && m.replyOrigUser == Roboto.Settings.botUserName)
                {
                    mod_xyzzy_player p = chatData.getPlayer(m.text_msg);
                    if (p != null)
                    {
                        chatData.players.Remove(p);
                        TelegramAPI.SendMessage(m.chatID, "Kicked " + p.name, false, m.message_id, true);
                    }
                    chatData.check();

                    

                    processed = true;
                }




                if (m.text_msg.StartsWith("/xyzzy_start") && chatData.status == mod_xyzzy_data.statusTypes.Stopped)
                {
                    //Start a new game!
                    chatData.reset();
                    localData.clearExpectedReplies(c.chatID);
                    chatData.status = mod_xyzzy_data.statusTypes.SetGameLength;
                    //add the player that started the game
                    chatData.addPlayer(new mod_xyzzy_player(m.userFullName, m.userID));

                    //send out invites
                    TelegramAPI.GetReply(m.chatID, m.userFullName + " is starting a new game of xyzzy! Type /xyzzy_join to join. You can join / leave " +
                        "at any time - you will be included next time a question is asked. You will need to open a private chat to " + 
                        Roboto.Settings.botUserName + " if you haven't got one yet - unfortunately I am a stupid bot and can't do it myself :(" 
                        , -1, true);

                    //confirm number of questions
                    int nrQuestionID = TelegramAPI.GetReply(m.userID, "How many questions do you want the round to last for (-1 for infinite)", -1, true);
                    localData.expectedReplies.Add(new mod_xyzzy_expectedReply(nrQuestionID, m.userID, c.chatID, "")); //this will last until the game is started. 

                }

                //Set up the game, once we get a reply from the user. 
                else if (chatData.status == mod_xyzzy_data.statusTypes.SetGameLength && expectedInReplyTo != null)
                {
                    int questions;

                    if (int.TryParse(m.text_msg, out questions) && questions >= -1)
                    {
                        chatData.enteredQuestionCount = questions;
                        //next, ask which packs they want:
                        chatData.sendPackFilterMessage(m);
                        chatData.status = mod_xyzzy_data.statusTypes.setPackFilter;
                    }
                    else
                    {
                        TelegramAPI.GetReply(m.chatID, "Thats not a valid number", m.message_id, true);
                        TelegramAPI.GetReply(m.chatID, "How many questions do you want the round to last for? -1 for infinite", m.message_id, true);
                    }
                }

                //Set up the game filter, once we get a reply from the user. 
                else if (chatData.status == mod_xyzzy_data.statusTypes.setPackFilter && expectedInReplyTo != null)
                {
                    if (m.text_msg != "Continue")
                    {
                        chatData.setPackFilter(m);
                        chatData.sendPackFilterMessage(m);   
                    }
                    else if (chatData.packFilter.Count == 0)
                    {
                        chatData.sendPackFilterMessage(m);
                    }
                    else
                    {

                        chatData.addQuestions();

                        List<mod_xyzzy_card> answers = new List<mod_xyzzy_card>();
                        foreach (mod_xyzzy_card a in localData.answers)
                        {
                            if (chatData.packEnabled(a.category)) { answers.Add(a); }
                        }
                        //TODO - shuffle the list
                        foreach (mod_xyzzy_card answer in answers)
                        {
                            chatData.remainingAnswers.Add(answer.uniqueID);
                        }

                        //tell the player they can start when they want
                        string keyboard = TelegramAPI.createKeyboard(new List<string> { "start" },1);
                        int expectedMessageID = TelegramAPI.GetReply(m.userID, "OK, to start the game once enough players have joined reply to this with \"start\". You'll be sent a message when a user joins.", -1, true, keyboard);
                        chatData.status = mod_xyzzy_data.statusTypes.Invites;
                    }
                }


                //start the game proper
                else if (chatData.status == mod_xyzzy_data.statusTypes.Invites && expectedInReplyTo != null && m.text_msg == "start" )
                {
                    localData.clearExpectedReplies(c.chatID);
                    if (chatData.players.Count > 0) //TODO - should be min 2
                    {
                        chatData.askQuestion();
                    }
                    else
                    {
                        TelegramAPI.SendMessage(m.chatID, "Not enough players");
                    }
                    processed = true;
                }

                //A player answering the question
                else if (chatData.status == mod_xyzzy_data.statusTypes.Question && expectedInReplyTo != null)
                {
                    bool answerAccepted = chatData.logAnswer(m.userID, m.text_msg);
                    /*if (answerAccepted) - covered in the logAnswer step
                    {
                        //no longer expecting a reply from this player
                        if (chatData.allPlayersAnswered())
                        {
                            chatData.beginJudging();
                        }
                    }
                    */
                }

                //A judges response
                else if (chatData.status == mod_xyzzy_data.statusTypes.Judging && expectedInReplyTo != null)
                {
                    bool success = chatData.judgesResponse(m.text_msg);
                    if (success)
                    {
                        //no longer expecting a reply from this player
                        localData.expectedReplies.Remove(expectedInReplyTo);
                    }
                }
                //player joining
                else if (m.text_msg.StartsWith("/xyzzy_join"))
                {
                    //TODO - try send a test message. If it fails, tell the user to open a 1:1 chat.
                    int i = -1;
                    try
                    {
                        i = TelegramAPI.SendMessage(m.userID, "You joined the xyzzy game in " + m.chatName);
                    }
                    catch
                    {
                        TelegramAPI.SendMessage(m.chatID, "Couldn't add " + m.userFullName + " to the game, as I couldnt send them a message. " 
                            + m.userFullName + " probably needs to open a chat session with me. "
                            + "Create a message session, then try /xyzzy_join again. Asshole.", false, m.message_id);
                    }

                    if (i != -1)
                    {
                        bool added = chatData.addPlayer(new mod_xyzzy_player(m.userFullName, m.userID));
                        if (added) { TelegramAPI.SendMessage(c.chatID, m.userFullName + " has joined the game"); }
                        else { TelegramAPI.SendMessage(c.chatID, m.userFullName + " is already in the game"); }
                    }
                    processed = true;
                }
                //player leaving
                else if (m.text_msg.StartsWith("/xyzzy_leave"))
                {
                    bool removed = chatData.removePlayer(m.userID);
                    if (removed) { TelegramAPI.SendMessage(c.chatID, m.userFullName + " has left the game"); }
                    else { TelegramAPI.SendMessage(c.chatID, m.userFullName + " isnt part of the game, and can't be removed!"); }
                    processed = true;
                }
                //player kicked
                else if (m.text_msg.StartsWith("/xyzzy_kick"))
                {
                    List<string> players = new List<string>();
                    foreach (mod_xyzzy_player p in chatData.players) {players.Add(p.name);}
                    string keyboard = TelegramAPI.createKeyboard(players,2);
                    TelegramAPI.GetReply(m.chatID, "Which player do you want to kick", m.message_id, true, keyboard);
                    processed = true;
                }
                //player kicked
                else if (m.text_msg.StartsWith("/xyzzy_abandon") && chatData.status != mod_xyzzy_data.statusTypes.Stopped)
                {
                    chatData.status = mod_xyzzy_data.statusTypes.Stopped;
                    localData.clearExpectedReplies(c.chatID);
                    TelegramAPI.SendMessage(c.chatID, "Game abandoned. type /xyzzy_start to start a new game.");
                    processed = true;
                }
                //extend a game
                else if (m.text_msg.StartsWith("/xyzzy_extend"))
                {
                    chatData.addQuestions();

                    TelegramAPI.SendMessage(c.chatID, "Added additional cards to the game!");
                    if (chatData.status == mod_xyzzy_data.statusTypes.Stopped)
                    {
                        chatData.askQuestion();
                    }

                    processed = true;
                }


                //debug question
                else if (m.text_msg.StartsWith("/xyzzy_question") && chatData.status != mod_xyzzy_data.statusTypes.Stopped)
                {
                    //TODO - DEBUG ONLY
                    chatData.askQuestion();
                    processed = true;
                }
                else if (m.text_msg.StartsWith("/xyzzy_status"))
                {
                    string response = "The current status of the game is " + chatData.status.ToString();
                    if (chatData.status != mod_xyzzy_data.statusTypes.Stopped)
                    {
                        response += " with " + chatData.remainingQuestions.Count.ToString() + " questions remaining. Say /xyzzy_join to join. The following players are currently playing: \n\r";
                        foreach (mod_xyzzy_player p in chatData.players)
                        {
                            response += p.name + " - " + p.wins.ToString() + " points. \n\r";
                        }
                        
                        switch (chatData.status)
                        {
                            case mod_xyzzy_data.statusTypes.Question:
                                response += "The current question is : " + "\n\r" +
                                    localData.getQuestionCard(chatData.currentQuestion).text + "\n\r" +
                                    "The following responses are outstanding :";
                                foreach (mod_xyzzy_expectedReply r in localData.expectedReplies)
                                {
                                    if (r.chatID == c.chatID)
                                    {
                                        mod_xyzzy_player p = chatData.getPlayer(r.playerID);
                                        if (p != null) { response += " " + p.name; }
                                    }
                                }
                                break;

                            case mod_xyzzy_data.statusTypes.Judging:
                                response += "Waiting for " + chatData.players[chatData.lastPlayerAsked].name + " to judge";
                                break;
                        }
                    }
                    
                    TelegramAPI.SendMessage(m.chatID, response, false, m.message_id,true);
                    chatData.check();
                    processed = true;
                }
                else if (m.text_msg.StartsWith("/xyzzy_filter"))
                {
                    string response = "The following pack filters are currently set. These can be changed when starting a new game : " + "\n\r" +
        chatData.getPackFilterStatus();
                    TelegramAPI.SendMessage(m.chatID, response, false, m.message_id);
                    processed = true;
                }

            }
            
               
            return processed;
        }


        /// <summary>
        /// Gets card positions that havent already been picked. 
        /// </summary>
        /// <param name="p"></param>
        /// <param name="questions"></param>
        /// <returns></returns>
        public static List<int> getUniquePositions(int arraySize, int questions)
        {
            //TODO - generic
            List<int> results = new List<int>();
            //create a dummy array
            List<int> dummy = new List<int>();
            for (int i = 0; i < arraySize; i++){dummy.Add(i);}

            //pick from the array, removing the picked number
            for (int i = 0; i < questions; i++)
            {
                int newCardPos = settings.getRandom(dummy.Count);
                results.Add(dummy[newCardPos]);
                dummy.Remove(newCardPos);
            }

            return results;
        }

        protected override void backgroundProcessing()
        {
        }


        public override void sampleData()
        {
            //cards sourced from https://raw.githubusercontent.com/samurailink3/hangouts-against-humanity/master/source/data/cards.js

            #region sampledata
            localData.answers.Add(new mod_xyzzy_card("Flying sex snakes.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Michelle Obama's arms.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("German dungeon porn.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("White people.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Getting so angry that you pop a boner.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Tasteful sideboob.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Praying the gay away.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Two midgets shitting into a bucket.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("MechaHitler.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Being a motherfucking sorcerer.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A disappointing birthday party.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Puppies!", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A windmill full of corpses.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Guys who don't call.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Racially-biased SAT questions.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Dying.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Steven Hawking talking dirty.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Being on fire.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A lifetime of sadness.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("An erection that lasts longer than four hours.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("AIDS", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Same-sex ice dancing.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Glenn Beck catching his scrotum on a curtain hook.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The Rapture.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Pterodactyl eggs.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Crippling debt.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Eugenics.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Exchanging pleasantries.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Dying of dysentery.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Roofies.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Getting naked and watching Nickelodeon.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The forbidden fruit.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Republicans.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The Big Bang.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Anal beads.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Amputees.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Men.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Surprise sex!", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Kim Jong-il.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Concealing a boner", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Agriculture.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Glenn Beck being harried by a swarm of buzzards.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Making a pouty face.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A salty surprise.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The Jews.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Charisma.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("YOU MUST CONSTRUCT ADDITIONAL PYLONS.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Panda sex.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Taking off your shirt.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A drive-by shooting.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Ronald Reagan.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Morgan Freeman's voice.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Breaking out into song and dance.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Jewish fraternities.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Dead babies.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Masturbation.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Hormone injections.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("All-you-can-eat shrimp for $4.99.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Incest.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Scalping.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Soup that is too hot.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The &Uuml;bermensch.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Nazis.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Tom Cruise.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Stifling a giggle at the mention of Hutus and Tutsis.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Edible underpants.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The Hustle.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A Super Soaker&trade; full of cat pee.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Figgy pudding.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Object permanence.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Consultants.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Intelligent design.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Nocturnal emissions.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Uppercuts.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Being marginalized.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The profoundly handicapped.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Obesity.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Chutzpah.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Unfathomable stupidity.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Repression.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Attitude.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Passable transvestites.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Party poopers.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The American Dream", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Child beauty pageants.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Puberty.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Testicular torsion.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The folly of man.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Nickelback.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Swooping.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Goats eating cans.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The KKK.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Kamikaze pilots.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Horrifying laser hair removal accidents.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Adderall&trade;.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A look-see.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Doing the right thing.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The taint; the grundle; the fleshy fun-bridge.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Lactation.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Pabst Blue Ribbon.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Powerful thighs.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Saxophone solos.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The gays.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A middle-aged man on roller skates.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A foul mouth.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The thing that electrocutes your abs.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Heteronormativity.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Cuddling.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Coat hanger abortions.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A big hoopla about nothing.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Boogers.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A hot mess.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Raptor attacks.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("My collection of high-tech sex toys.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Fear itself.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Bees?", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Getting drunk on mouthwash.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Sniffing glue.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Oversized lollipops.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("An icepick lobotomy.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Being rich.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Friends with benefits.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Teaching a robot to love.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Women's suffrage.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Me time.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The heart of a child.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Smallpox blankets.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The clitoris.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("John Wilkes Booth.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The glass ceiling.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Sarah Palin.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Sexy pillow fights.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Yeast.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Full frontal nudity.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Parting the Red Sea.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A Bop It&trade;.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Michael Jackson.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Team-building exercises.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Harry Potter erotica.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Authentic Mexican cuisine.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Frolicking.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Sexting.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Grandma.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Not giving a shit about the Third World.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Licking things to claim them as your own.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Genghis Khan.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The hardworking Mexican.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("RoboCop.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("My relationship status.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Scrubbing under the folds.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Porn Stars.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Horse meat.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Sunshine and rainbows.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Expecting a burp and vomiting on the floor.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Barack Obama.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Spontaneous human combustion.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Natural selection.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Mouth herpes.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Flash flooding.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Goblins.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A monkey smoking a cigar.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Spectacular abs.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A good sniff.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Wiping her butt.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The Three-Fifths compromise.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Pedophiles.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Doin' it in the butt.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Being fabulous.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Mathletes.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Wearing underwear inside-out to avoid doing laundry.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Nipple blades.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("An M. Night Shyamalan plot twist.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A bag of magic beans.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Vigorous jazz hands.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A defective condom.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Skeletor.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Vikings.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Leaving an awkward voicemail.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Teenage pregnancy.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Dead parents.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Hot cheese.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("My sex life.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A mopey zoo lion.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Assless chaps.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Riding off into the sunset.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Lance Armstrong's missing testicle.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Sweet, sweet vengeance.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Genital piercings.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Keg stands.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Darth Vader.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Viagra&reg;.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Necrophilia.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A really cool hat.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Toni Morrison's vagina.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("An Oedipus complex.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The Tempur-Pedic&reg; Swedish Sleep System&trade;.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Preteens.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Dick fingers.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A cooler full of organs.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Shapeshifters.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The Care Bear Stare.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Erectile dysfunction.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Keanu Reeves.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The Virginia Tech Massacre.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The Underground Railroad.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The chronic.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Queefing.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Heartwarming orphans.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A thermonuclear detonation.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Cheating in the Special Olympics.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Tangled Slinkys.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A moment of silence.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Civilian casualties.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Catapults.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Sharing needles.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Ethnic cleansing.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Emotions.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Children on leashes.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Balls.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Homeless people.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Eating all of the cookies before the AIDS bake-sale.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Old-people smell.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Farting and walking away.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The inevitable heat death of the universe.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The violation of our most basic human rights.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Fingering.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The placenta.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The Rev. Dr. Martin Luther King, Jr.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Leprosy.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Sperm whales.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Multiple stab wounds.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Flightless birds.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Grave robbing.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Home video of Oprah sobbing into a Lean Cuisine&reg;.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Oompa-Loompas.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A murder most foul.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Tentacle porn.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Daddy issues.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Bill Nye the Science Guy.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Peeing a little bit.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The miracle of childbirth.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Tweeting.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Another goddamn vampire movie.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Britney Spears at 55.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("New Age music.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The Make-A-Wish&reg; Foundation.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Firing a rifle into the air while balls deep in a squealing hog.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Active listening.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Nicolas Cage.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("72 virgins.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Stranger danger.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Hope.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A gassy antelope.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("BATMAN!!!", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Chivalry.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Passing a kidney stone.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Black people.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Natalie Portman.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A mime having a stroke.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Classist undertones.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Sean Penn.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A mating display.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The Holy Bible.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Hot Pockets&reg;.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A sad handjob.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Pulling out.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Serfdom.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Pixelated bukkake.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Dropping a chandelier on your enemies and riding the rope up.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Jew-fros.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Waiting 'til marriage.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Euphoria&trade; by Calvin Klein.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The World of Warcraft.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Lunchables&trade;.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The Kool-Aid Man.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The Trail of Tears.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Self-loathing.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A falcon with a cap on its head.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Historically black colleges.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Not reciprocating oral sex.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Global warming.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Ghosts.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("World peace.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A can of whoop-ass.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The Dance of the Sugar Plum Fairy.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A zesty breakfast burrito.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Switching to Geico&reg;.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Aaron Burr.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Picking up girls at the abortion clinic.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Land mines.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Former President George W. Bush.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Geese.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Mutually-assured destruction.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("College.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Date rape.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Bling.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A gentle caress of the inner thigh.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A time travel paradox.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Seppuku.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Poor life choices.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Waking up half-naked in a Denny's parking lot.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Italians.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("GoGurt&reg;.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Finger painting.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Robert Downey, Jr.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("My soul.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Smegma.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Embryonic stem cells.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The South.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Christopher Walken.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Gloryholes.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Pretending to care.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Public ridicule.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A tiny horse.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Arnold Schwarzenegger.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A stray pube.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Jerking off into a pool of children's tears.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Child abuse.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Glenn Beck convulsively vomiting as a brood of crab spiders hatches in his brain and erupts from his tear ducts.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Menstruation.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A sassy black woman.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Re-gifting.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Penis envy.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A sausage festival.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Getting really high.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Drinking alone.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Too much hair gel.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Hulk Hogan.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Overcompensation.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Foreskin.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Free samples.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Shaquille O'Neal's acting career.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Five-Dollar Footlongs&trade;.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Whipping it out.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A snapping turtle biting the tip of your penis.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Muhammad (Praise Be Unto Him).", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Half-assed foreplay.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Dental dams.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Being a dick to children.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Famine.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Chainsaws for hands.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A gypsy curse.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("AXE Body Spray.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The Force.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Explosions.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Cybernetic enhancements.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Customer service representatives.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("White privilege.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Gandhi.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Road head.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("God.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Poorly-timed Holocaust jokes.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("8 oz. of sweet Mexican black-tar heroin.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Judge Judy.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The Little Engine That Could.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Altar boys.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Mr. Clean, right behind you.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Vehicular manslaughter.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Dwarf tossing.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Friction.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Lady Gaga.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Scientology.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Justin Bieber.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A death ray.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Vigilante justice.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The Pope.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A sea of troubles.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Alcoholism.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Poor people.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A fetus.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Women in yogurt commercials.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Exactly what you'd expect.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Flesh-eating bacteria.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("My genitals.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A balanced breakfast.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Dick Cheney.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Lockjaw.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Natural male enhancement.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Take-backsies.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Winking at old people.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Opposable thumbs.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Flying sex snakes.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Passive-aggressive Post-it notes.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Inappropriate yodeling.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Golden showers.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Racism.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Copping a feel.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Auschwitz.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Elderly Japanese men.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Raping and pillaging.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Kids with ass cancer.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Pictures of boobs.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The homosexual agenda.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A homoerotic volleyball montage.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Sexual tension.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Hurricane Katrina.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Fiery poops.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Science.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Dry heaving.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Cards Against Humanity.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Fancy Feast&reg;.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A bleached asshole.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Lumberjack fantasies.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("American Gladiators.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Autocannibalism.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Sean Connery.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("William Shatner.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Domino's&trade; Oreo&trade; Dessert Pizza.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("An asymmetric boob job.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Centaurs.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A micropenis.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Asians who aren't good at math.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The milk man.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Waterboarding.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Wifely duties.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Loose lips.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The Blood of Christ.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Actually taking candy from a baby.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The token minority.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Jibber-jabber.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A brain tumor.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Bingeing and purging.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A clandestine butt scratch.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The Chinese gymnastics team.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Prancing.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The Hamburglar.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Police brutality.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Man meat.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Forgetting the Alamo.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Eating the last known bison.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Crystal meth.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Booby-trapping the house to foil burglars.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("My inner demons.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Third base.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Soiling oneself.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Laying an egg.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Giving 110%.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Hot people.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Friendly fire.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Count Chocula.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Pac-Man uncontrollably guzzling cum.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Estrogen.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("My vagina.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Kanye West.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("A robust mongoloid.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The Donald Trump Seal of Approval&trade;.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The true meaning of Christmas.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Her Royal Highness, Queen Elizabeth II.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("An honest cop with nothing left to lose.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Feeding Rosie O'Donnell.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The Amish.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("The terrorists.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("When you fart and a little bit comes out.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Pooping back and forth. Forever.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Friends who eat all the snacks.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Cockfights.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Bitches.", " Base"));
            localData.answers.Add(new mod_xyzzy_card("Seduction.", " Base"));
            localData.questions.Add(new mod_xyzzy_card("_?  There's an app for that.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("Why can't I sleep at night?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("What's that smell?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("I got 99 problems but _ ain't one.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("Maybe she's born with it.  Maybe it's _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("What's the next Happy Meal&reg; toy?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("Anthropologists have recently discovered a primitive tribe that worships _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("It's a pity that kids these days are all getting involved with _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("During Picasso's often-overlooked Brown Period, he produced hundreds of paintings of _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("Alternative medicine is now embracing the curative powers of _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("And the Academy Award for _ goes to _.", " Base", 2));
            localData.questions.Add(new mod_xyzzy_card("What's that sound?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("What ended my last relationship?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("MTV's new reality show features eight washed-up celebrities living with _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("I drink to forget _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("I'm sorry professor, but I couldn't complete my homework because of _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("What is Batman's guilty pleasure?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("This is the way the world ends <br> This is the way the world ends <br> Not with a bang but with _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("What's a girl's best friend?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("TSA guidelines now prohibit _ on airplanes.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("_.  That's how I want to die.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("For my next trick, I will pull _ out of _.", " Base", 2));
            localData.questions.Add(new mod_xyzzy_card("In the new Disney Channel Original Movie, Hannah Montana struggles with _ for the first time.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("_ is a slippery slope that leads to _.", " Base", 2));
            localData.questions.Add(new mod_xyzzy_card("What does Dick Cheney prefer?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("Dear Abby, I'm having some trouble with _ and would like your advice.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("Instead of coal, Santa now gives the bad children _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("What's the most emo?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("In 1,000 years when paper money is but a distant memory, _ will be our currency.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("What's the next superhero/sidekick duo?", " Base", 2));
            localData.questions.Add(new mod_xyzzy_card("In M. Night Shyamalan's new movie, Bruce Willis discovers that _ had really been _ all along.", " Base", 2));
            localData.questions.Add(new mod_xyzzy_card("A romantic, candlelit dinner would be incomplete without _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("_.  Becha can't have just one!", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("White people like _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("_.  High five, bro.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("Next from J.K. Rowling: Harry Potter and the Chamber of _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("BILLY MAYS HERE FOR _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("In a world ravaged by _, our only solace is _.", " Base", 2));
            localData.questions.Add(new mod_xyzzy_card("War!  What is it good for?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("During sex, I like to think about _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("What are my parents hiding from me?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("What will always get you laid?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("In L.A. County Jail, word is you can trade 200 cigarettes for _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("What did I bring back from Mexico?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("What don't you want to find in your Chinese food?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("What will I bring back in time to convince people that I am a powerful wizard?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("How am I maintaining my relationship status?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("_.  It's a trap!", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("Coming to Broadway this season, _: The Musical.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("While the United States raced the Soviet Union to the moon, the Mexican government funneled millions of pesos into research on _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("After the earthquake, Sean Penn brought _ to the people of Haiti.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("Next on ESPN2, the World Series of _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("Step 1: _.  Step 2: _.  Step 3: Profit.", " Base", 2));
            localData.questions.Add(new mod_xyzzy_card("Rumor has it that Vladimir Putin's favorite dish is _ stuffed with _.", " Base", 2));
            localData.questions.Add(new mod_xyzzy_card("But before I kill you, Mr. Bond, I must show you _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("What gives me uncontrollable gas?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("What do old people smell like?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("The class field trip was completely ruined by _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("When Pharaoh remained unmoved, Moses called down a Plague of _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("What's my secret power?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("What's there a ton of in heaven?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("What would grandma find disturbing, yet oddly charming?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("I never truly understood _ until I encountered _.", " Base", 2));
            localData.questions.Add(new mod_xyzzy_card("What did the U.S. airdrop to the children of Afghanistan?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("What helps Obama unwind?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("What did Vin Diesel eat for dinner?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("_: good to the last drop.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("Why am I sticky?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("What gets better with age?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("_: kid-tested, mother-approved.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("What's the crustiest?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("What's Teach for America using to inspire inner city students to succeed?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("Studies show that lab rats navigate mazes 50% faster after being exposed to _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("Life for American Indians was forever changed when the White Man introduced them to _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("Make a haiku.", " Base", 3));
            localData.questions.Add(new mod_xyzzy_card("I do not know with what weapons World War III will be fought, but World War IV will be fought with _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("Why do I hurt all over?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("What am I giving up for Lent?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("In Michael Jackson's final moments, he thought about _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("In an attempt to reach a wider audience, the Smithsonian Museum of Natural History has opened an interactive exhibit on _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("When I am President of the United States, I will create the Department of _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("Lifetime&reg; presents _, the story of _.", " Base", 2));
            localData.questions.Add(new mod_xyzzy_card("When I am a billionaire, I shall erect a 50-foot statue to commemorate _.", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("When I was tripping on acid, _ turned into _.", " Base", 2));
            localData.questions.Add(new mod_xyzzy_card("That's right, I killed _.  How, you ask?  _.", " Base", 2));
            localData.questions.Add(new mod_xyzzy_card("What's my anti-drug?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("_ + _ = _.", " Base", 3));
            localData.questions.Add(new mod_xyzzy_card("What never fails to liven up the party?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("What's the new fad diet?", " Base", 1));
            localData.questions.Add(new mod_xyzzy_card("Major League Baseball has banned _ for giving players an unfair advantage.", " Base", 1));
            localData.answers.Add(new mod_xyzzy_card("A big black dick.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("A beached whale.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("A bloody pacifier.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("A crappy little hand.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("A low standard of living.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("A nuanced critique.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Panty raids.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("A passionate Latino lover.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("A rival dojo.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("A web of lies.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("A woman scorned.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Clams.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Apologizing.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("A plunger to the face.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Neil Patrick Harris.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Beating your wives.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Being a dinosaur.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Shaft.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Bosnian chicken farmers.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Nubile slave boys.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Carnies.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Coughing into a vagina.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Suicidal thoughts.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("The ooze.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Deflowering the princess.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Dorito breath.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Eating an albino.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Enormous Scandinavian women.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Fabricating statistics.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Finding a skeleton.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Gandalf.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Genetically engineered super-soldiers.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("George Clooney's musk.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Getting abducted by Peter Pan.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Getting in her pants, politely.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Gladiatorial combat.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Clenched butt cheeks.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Hipsters.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Historical revisionism.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Insatiable bloodlust.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Jafar.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Jean-Claude Van Damme.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Just the tip.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Mad hacky-sack skills.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Leveling up.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Literally eating shit.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Making the penises kiss.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("24-hour media coverage.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Medieval Times&copy; Dinner & Tournament.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Moral ambiguity.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("My machete.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("One thousand Slim Jims.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Ominous background music.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Overpowering your father.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Stockholm Syndrome.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Quiche.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Quivering jowls.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Revenge fucking.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Ripping into a man's chest and pulling out his still-beating heart.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Ryan Gosling riding in on a white horse.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Santa Claus.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Scrotum tickling.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Sexual humiliation.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Sexy Siamese twins.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Saliva.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Space muffins.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Statistically validated stereotypes.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Sudden Poop Explosion Disease.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("The boners of the elderly.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("The economy.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Syphilitic insanity.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("The Gulags.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("The harsh light of day.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("The hiccups.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("The shambling corpse of Larry King.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("The four arms of Vishnu.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Being a busy adult with many important things to do.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Tripping balls.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Words, words, words.", " CAHe1"));
            localData.answers.Add(new mod_xyzzy_card("Zeus's sexual appetites.", " CAHe1"));
            localData.questions.Add(new mod_xyzzy_card("My plan for world domination begins with _.", " CAHe1", 1));
            localData.questions.Add(new mod_xyzzy_card("The CIA now interrogates enemy agents by repeatedly subjecting them to _.", " CAHe1", 1));
            localData.questions.Add(new mod_xyzzy_card("Dear Sir or Madam, We regret to inform you that the Office of _ has denied your request for _", " CAHe1", 2));
            localData.questions.Add(new mod_xyzzy_card("In Rome, there are whisperings that the Vatican has a secret room devoted to _.", " CAHe1", 1));
            localData.questions.Add(new mod_xyzzy_card("Science will never explain _.", " CAHe1", 1));
            localData.questions.Add(new mod_xyzzy_card("When all else fails, I can always masturbate to _.", " CAHe1", 1));
            localData.questions.Add(new mod_xyzzy_card("I learned the hard way that you can't cheer up a grieving friend with _.", " CAHe1", 1));
            localData.questions.Add(new mod_xyzzy_card("In its new tourism campaign, Detroit proudly proclaims that it has finally eliminated _.", " CAHe1", 1));
            localData.questions.Add(new mod_xyzzy_card("An international tribunal has found _ guilty of _.", " CAHe1", 2));
            localData.questions.Add(new mod_xyzzy_card("The socialist governments of Scandinavia have declared that access to _ is a basic human right.", " CAHe1", 1));
            localData.questions.Add(new mod_xyzzy_card("In his new self-produced album, Kanye West raps over the sounds of _.", " CAHe1", 1));
            localData.questions.Add(new mod_xyzzy_card("What's the gift that keeps on giving?", " CAHe1", 1));
            localData.questions.Add(new mod_xyzzy_card("Next season on Man vs. Wild, Bear Grylls must survive in the depths of the Amazon with only _ and his wits.", " CAHe1", 1));
            localData.questions.Add(new mod_xyzzy_card("When I pooped, what came out of my butt?", " CAHe1", 1));
            localData.questions.Add(new mod_xyzzy_card("In the distant future, historians will agree that _ marked the beginning of America's decline.", " CAHe1", 1));
            localData.questions.Add(new mod_xyzzy_card("In a pinch, _ can be a suitable substitute for _.", " CAHe1", 2));
            localData.questions.Add(new mod_xyzzy_card("What has been making life difficult at the nudist colony?", " CAHe1", 1));
            localData.questions.Add(new mod_xyzzy_card("Michael Bay's new three-hour action epic pits _ against _.", " CAHe1", 2));
            localData.questions.Add(new mod_xyzzy_card("And I would have gotten away with it, too, if it hadn't been for _.", " CAHe1", 1));
            localData.questions.Add(new mod_xyzzy_card("What brought the orgy to a grinding halt?", " CAHe1", 1));
            localData.answers.Add(new mod_xyzzy_card("A bigger, blacker dick.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("The mere concept of Applebee's&reg;.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("A sad fat dragon with no friends.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Catastrophic urethral trauma.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Hillary Clinton's death stare.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Existing.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("A pinata full of scorpions.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Mooing.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Swiftly achieving orgasm.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Daddy's belt.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Double penetration.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Weapons-grade plutonium.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Some really fucked-up shit.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Subduing a grizzly bear and making her your wife.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Rising from the grave.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("The mixing of the races.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Taking a man's eyes and balls out and putting his eyes where his balls go and then his balls in the eye holes.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Scrotal frostbite.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("All of this blood.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Loki, the trickster god.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Whining like a little bitch.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Pumping out a baby every nine months.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Tongue.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Finding Waldo.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Upgrading homeless people to mobile hotspots.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Wearing an octopus for a hat.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("An unhinged ferris wheel rolling toward the sea.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Living in a trashcan.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("The corporations.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("A magic hippie love cloud.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Fuck Mountain.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Survivor's guilt.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Me.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Getting hilariously gang-banged by the Blue Man Group.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Jeff Goldblum.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Making a friend.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("A soulful rendition of &#34;Ol' Man River.&#34;", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Intimacy problems.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("A sweaty, panting leather daddy.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Spring break!", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Being awesome at sex.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Dining with cardboard cutouts of the cast of &#34;Friends.&#34;", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Another shot of morphine.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Beefin' over turf.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("A squadron of moles wearing aviator goggles.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Bullshit.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("The Google.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Pretty Pretty Princess Dress-Up Board Game&#174;.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("The new Radiohead album.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("An army of skeletons.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("A man in yoga pants with a ponytail and feather earrings.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Mild autism.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Nunchuck moves.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Whipping a disobedient slave.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("An ether-soaked rag.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("A sweet spaceship.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("A 55-gallon drum of lube.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Special musical guest, Cher.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("The human body.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Boris the Soviet Love Hammer.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("The grey nutrient broth that sustains Mitt Romney.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Tiny nipples.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Power.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Oncoming traffic.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("A dollop of sour cream.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("A slightly shittier parallel universe.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("My first kill.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Graphic violence, adult language, and some sexual content.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Fetal alcohol syndrome.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("The day the birds attacked.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("One Ring to rule them all.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Grandpa's ashes.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Basic human decency.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("A Burmese tiger pit.", " CAHe2"));
            localData.answers.Add(new mod_xyzzy_card("Death by Steven Seagal", " CAHe2"));
            localData.questions.Add(new mod_xyzzy_card("During his midlife crisis, my dad got really into _.", " CAHe2", 1));
            localData.questions.Add(new mod_xyzzy_card("_ would be woefully incomplete without _.", " CAHe2", 2));
            localData.questions.Add(new mod_xyzzy_card("My new favorite porn star is Joey &#34;_&#34; McGee.", " CAHe2", 1));
            localData.questions.Add(new mod_xyzzy_card("Before I run for president, I must destroy all evidence of my involvement with _.", " CAHe2", 1));
            localData.questions.Add(new mod_xyzzy_card("This is your captain speaking. Fasten your seatbelts and prepare for _.", " CAHe2", 1));
            localData.questions.Add(new mod_xyzzy_card("In his newest and most difficult stunt, David Blaine must escape from _.", " CAHe2", 1));
            localData.questions.Add(new mod_xyzzy_card("The Five Stages of Grief: denial, anger, bargaining, _, and acceptance.", " CAHe2", 1));
            localData.questions.Add(new mod_xyzzy_card("My mom freaked out when she looked at my browser history and found _.com/_.", " CAHe2", 2));
            localData.questions.Add(new mod_xyzzy_card("I went from _ to _, all thanks to _.", " CAHe2", 3));
            localData.questions.Add(new mod_xyzzy_card("Members of New York's social elite are paying thousands of dollars just to experience _.", " CAHe2", 1));
            localData.questions.Add(new mod_xyzzy_card("This month's Cosmo: &#34;Spice up your sex life by bringing _ into the bedroom.&#34;", " CAHe2", 1));
            localData.questions.Add(new mod_xyzzy_card("Little Miss Muffet Sat on a tuffet, Eating her curds and _.", " CAHe2", 1));
            localData.questions.Add(new mod_xyzzy_card("If God didn't want us to enjoy _, he wouldn't have given us _.", " CAHe2", 2));
            localData.questions.Add(new mod_xyzzy_card("My country, 'tis of thee, sweet land of _.", " CAHe2", 1));
            localData.questions.Add(new mod_xyzzy_card("After months of debate, the Occupy Wall Street General Assembly could only agree on &#34;More _!&#34;", " CAHe2", 1));
            localData.questions.Add(new mod_xyzzy_card("I spent my whole life working toward _, only to have it ruined by _.", " CAHe2", 2));
            localData.questions.Add(new mod_xyzzy_card("Next time on Dr. Phil: How to talk to your child about _.", " CAHe2", 1));
            localData.questions.Add(new mod_xyzzy_card("Only two things in life are certain: death and _.", " CAHe2", 1));
            localData.questions.Add(new mod_xyzzy_card("Everyone down on the ground! We don't want to hurt anyone. We're just here for _.", " CAHe2", 1));
            localData.questions.Add(new mod_xyzzy_card("The healing process began when I joined a support group for victims of _.", " CAHe2", 1));
            localData.questions.Add(new mod_xyzzy_card("The votes are in, and the new high school mascot is _.", " CAHe2", 1));
            localData.questions.Add(new mod_xyzzy_card("Charades was ruined for me forever when my mom had to act out _.", " CAHe2", 1));
            localData.questions.Add(new mod_xyzzy_card("Before _, all we had was _.", " CAHe2", 2));
            localData.questions.Add(new mod_xyzzy_card("Tonight on 20/20: What you don't know about _ could kill you.", " CAHe2", 1));
            localData.questions.Add(new mod_xyzzy_card("You haven't truly lived until you've experienced _ and _ at the same time.", " CAHe2", 2));
            localData.questions.Add(new mod_xyzzy_card("D&D 4.0 isn't real D&D because of the _.", "CAHgrognards", 1));
            localData.questions.Add(new mod_xyzzy_card("It's a D&D retroclone with _ added.", "CAHgrognards", 1));
            localData.questions.Add(new mod_xyzzy_card("Storygames aren't RPGs because of the _.", "CAHgrognards", 1));
            localData.questions.Add(new mod_xyzzy_card("The Slayer's Guide to _.", "CAHgrognards", 1));
            localData.questions.Add(new mod_xyzzy_card("Worst character concept ever: _, but with _.", "CAHgrognards", 2));
            localData.questions.Add(new mod_xyzzy_card("Alightment: Chaotic _", "CAHgrognards", 1));
            localData.questions.Add(new mod_xyzzy_card("It's a D&D retroclone with _ added.", "CAHgrognards", 1));
            localData.questions.Add(new mod_xyzzy_card("What made the paladin fall? _", "CAHgrognards", 1));
            localData.questions.Add(new mod_xyzzy_card("The portal leads to the quasi-elemental plane of _.", "CAHgrognards", 1));
            localData.questions.Add(new mod_xyzzy_card("The Temple of Elemental _.", "CAHgrognards", 1));
            localData.questions.Add(new mod_xyzzy_card("Pathfinder is basically D&D _ Edition.", "CAHgrognards", 1));
            localData.questions.Add(new mod_xyzzy_card("_ : The Storytelling Game.", "CAHgrognards", 1));
            localData.questions.Add(new mod_xyzzy_card("People are wondering why Steve Jackson published GURPS _.", "CAHgrognards", 1));
            localData.questions.Add(new mod_xyzzy_card("Linear Fighter, Quadratic _.", "CAHgrognards", 1));
            localData.questions.Add(new mod_xyzzy_card("You start with 1d4 _ points.", "CAHgrognards", 1));
            localData.questions.Add(new mod_xyzzy_card("Back when I was 12 and I was just starting playing D&D, the game had _.", "CAHgrognards", 1));
            localData.questions.Add(new mod_xyzzy_card("Big Eyes, Small _.", "CAHgrognards", 1));
            localData.questions.Add(new mod_xyzzy_card("In the grim darkness of the future there is only _.", "CAHgrognards", 1));
            localData.questions.Add(new mod_xyzzy_card("My innovative new RPG has a stat for _.", "CAHgrognards", 1));
            localData.questions.Add(new mod_xyzzy_card("A true gamer has no problem with _.", "CAHgrognards", 1));
            localData.questions.Add(new mod_xyzzy_card("Elminster cast a potent _ spell and then had sex with _.", "CAHgrognards", 2));
            localData.questions.Add(new mod_xyzzy_card("The Deck of Many _.", "CAHgrognards", 1));
            localData.questions.Add(new mod_xyzzy_card("You are all at a tavern when _ approach you.", "CAHgrognards", 1));
            localData.answers.Add(new mod_xyzzy_card("Dragon boobs.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Verisimilitude.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Dissociated mechanics.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Rape.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Storygames.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Random chargen", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("RPG.net.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Dice inserted somewhere painful.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("FATAL.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Ron Edwards' brain damage.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Boob plate armor.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Gamer chicks.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("GNS theory.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Drizzt.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("The entire Palladium Books&reg; Megaverse&trade;", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("BadWrongFun.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Misogynerds.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Cultural Marxism.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Pissing on Gary Gygax's grave.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Steve Jackson's beard.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Natural 20.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Rapenards.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("The Crisis of Treachery&trade;.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Game balance.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Fishmalks.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("A kick to the dicebags.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Bearded dwarven women.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Owlbear's tears.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Magic missile.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("THAC0.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Bigby's Groping Hands.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Drow blackface.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Save or die.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Swine.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("The Forge.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Healing Surges.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Gelatinous Cubes.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Total Party Kill.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Quoting Monty Python.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Dumbed down shit for ADD WoW babies.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Mike Mearls.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Comeliness.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Vampire: The Masquerade.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Rifts&trade;.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("The random prostitute table.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Dildo of Enlightenment +2", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Grognards Against Humanity.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Cthulhu.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("The naked succubus in the Monster Manual.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Role-playing and roll-playing.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Fun Tyrant.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("4rries.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Martial dailies.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Black Tokyo.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Killfuck Soulshitter.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Cheetoism.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Grimdark.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Kobolds.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Oozemaster.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Rocks fall, everyone dies.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Mark Rein&middot;Hagen.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Maid RPG.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Splugorth blind warrior women.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Dying during chargen.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Slaughtering innocent orc children.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Lesbian stripper ninjas.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Magical tea party.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Grinding levels.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Dice animism.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("White privilege.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Githyanki therapy.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Amber Diceless Roleplaying.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("A ratcatcher with a small but vicious dog.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Bribing the GM with sexual favors.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Eurocentric fantasy.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Sacred cows.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Gygaxian naturalism.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Special snowflakes.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Neckbeards.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Gazebos.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Lorraine Williams.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Nude larping.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Portable holes.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Steampunk bullshit.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Dump stats.", "CAHgrognards"));
            localData.answers.Add(new mod_xyzzy_card("Ale and whores.", "CAHgrognards"));
            localData.questions.Add(new mod_xyzzy_card("For the convention I cosplayed as Sailor Moon, except with _.", "CAHweeaboo", 1));
            localData.questions.Add(new mod_xyzzy_card("The worst part of Grave of the Fireflies is all the _.", "CAHweeaboo", 1));
            localData.questions.Add(new mod_xyzzy_card("In the Evangelion remake, Shinji has to deal with _.", "CAHweeaboo", 1));
            localData.questions.Add(new mod_xyzzy_card("Worst anime convention purchase ever? _.", "CAHweeaboo", 1));
            localData.questions.Add(new mod_xyzzy_card("While powering up Vegeta screamed, _!", "CAHweeaboo", 1));
            localData.questions.Add(new mod_xyzzy_card("You evaded my _ attack. Most impressive.", "CAHweeaboo", 1));
            localData.questions.Add(new mod_xyzzy_card("I downloaded a doujin where _ got into _.", "CAHweeaboo", 2));
            localData.questions.Add(new mod_xyzzy_card("The magical girl found out that the Power of Love is useless against _.", "CAHweeaboo", 1));
            localData.questions.Add(new mod_xyzzy_card("The Japanese government has spent billions of yen researching _.", "CAHweeaboo", 1));
            localData.questions.Add(new mod_xyzzy_card("In the dubbed version they changed _ into _.", "CAHweeaboo", 2));
            localData.questions.Add(new mod_xyzzy_card("_ is Best Pony.", "CAHweeaboo", 1));
            localData.questions.Add(new mod_xyzzy_card("The _ of Haruhi Suzumiya.", "CAHweeaboo", 1));
            localData.questions.Add(new mod_xyzzy_card("The new thing in Akihabara is fetish cafes where you can see girls dressed up as _.", "CAHweeaboo", 1));
            localData.questions.Add(new mod_xyzzy_card("Your drill can pierce _!", "CAHweeaboo", 1));
            localData.questions.Add(new mod_xyzzy_card("Avatar: The Last _ bender.", "CAHweeaboo", 1));
            localData.questions.Add(new mod_xyzzy_card("In the name of _ Sailor Moon will punish you!", "CAHweeaboo", 1));
            localData.questions.Add(new mod_xyzzy_card("No harem anime is complete without _.", "CAHweeaboo", 1));
            localData.questions.Add(new mod_xyzzy_card("My boyfriend's a _ now.", "CAHweeaboo", 1));
            localData.questions.Add(new mod_xyzzy_card("The _ of _ has left me in despair!", "CAHweeaboo", 2));
            localData.questions.Add(new mod_xyzzy_card("_.tumblr.com", "CAHweeaboo", 1));
            localData.questions.Add(new mod_xyzzy_card("Somehow they made a cute mascot girl out of _.", "CAHweeaboo", 1));
            localData.questions.Add(new mod_xyzzy_card("Haruko hit Naoto in the head with her bass guitar and _ came out.", "CAHweeaboo", 1));
            localData.answers.Add(new mod_xyzzy_card("Japanese schoolgirl porn.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Horny catgirls.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Japanese people.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Cimo.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("ZA WARUDO!", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("40 gigs of lolicon.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Goku's hair.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Slashfic.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Star Gentle Uterus", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Naruto headbands.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Homestuck troll horns.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Hayao Miyazaki.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("The tsunami.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Death Note.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Small breasts.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Asians being racist against each other.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Weeaboo bullshit.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Tsundere.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Body pillows.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("A lifelike silicone love doll.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Anime figures drenched in jizz.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Surprise sex.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Yaoi.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Girls with glasses.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Bronies.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Blue and white striped panties.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("4chan.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Hello Kitty vibrator.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Finishing attack.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Keikaku* *(keikaku means plan).", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Hatsune Miku's screams.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("School swimsuits.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Lovingly animated bouncing boobs.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Dragon Balls.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Zangief's chest hair.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("DeviantArt.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Giant fucking robots.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Crossplay.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Moeblob.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Carl Macek's rotting corpse.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("My waifu.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Voice actress Megumi Hayashibara.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Lynn Minmei.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Panty shots.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Love and Justice.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Consensual tentacle rape.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Gundam.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Captain Bright slapping Amuro.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("The Wave Undulation Cannon.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Having sex in the P.E. equipment shed.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Tainted sushi.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Shitty eurobeat music.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Bad dubbing.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Fangirls.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Kawaii desu uguu.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Futanari.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Lesbian schoolgirls.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Osamu Tezuka, rolling in his grave forever.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("FUNimation.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Underage cosplayers in bondage gear.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Jackie Chan.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Exchanging Pocky for sexual favors.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Shipping.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Chiyo's father.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Magikarp.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Derpy.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Nanoha and her special friend Fate.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("The marbles from Ramune bottles.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Wideface.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Spoilers.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Man-Faye.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Oppai mousepads.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Another dimension.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Homura sniffing Madoka's panties.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Hadouken.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Asian ball-jointed dolls.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("J-list.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Childhood friends.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Monkey D. Luffy's rubbery cock.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Cloud's giant fucking Buster Swords.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Taking a dump in Char's helmet.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Hentai marathons.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Gothic Lolita.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Onaholes.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Super Saiyan Level 2.", "CAHweeaboo"));
            localData.answers.Add(new mod_xyzzy_card("Gaia Online.", "CAHweeaboo"));
            localData.questions.Add(new mod_xyzzy_card("After blacking out during New year's Eve, I was awoken by _.", "CAHxmas", 1));
            localData.questions.Add(new mod_xyzzy_card("This holiday season, Tim Allen must overcome his fear of _ to save Christmas.", "CAHxmas", 1));
            localData.questions.Add(new mod_xyzzy_card("Jesus is _.", "CAHxmas", 1));
            localData.questions.Add(new mod_xyzzy_card("Every Christmas, my uncle gets drunk and tells the story about _.", "CAHxmas", 1));
            localData.questions.Add(new mod_xyzzy_card("What keeps me warm during the cold, cold, winter?", "CAHxmas", 1));
            localData.questions.Add(new mod_xyzzy_card("On the third day of Christmas, my true love gave to me: three French hens, two turtle doves, and _.", "CAHxmas", 1));
            localData.questions.Add(new mod_xyzzy_card("Wake up, America. Christmas is under attack by secular liberals and their _.", "CAHxmas", 1));
            localData.answers.Add(new mod_xyzzy_card("Santa's heavy sack.", "CAHxmas"));
            localData.answers.Add(new mod_xyzzy_card("Clearing a bloody path through Walmart with a scimitar.", "CAHxmas"));
            localData.answers.Add(new mod_xyzzy_card("Another shitty year.", "CAHxmas"));
            localData.answers.Add(new mod_xyzzy_card("Whatever Kwanzaa is supposed to be about.", "CAHxmas"));
            localData.answers.Add(new mod_xyzzy_card("A Christmas stocking full of coleslaw.", "CAHxmas"));
            localData.answers.Add(new mod_xyzzy_card("Elf cum.", "CAHxmas"));
            localData.answers.Add(new mod_xyzzy_card("The tiny, calloused hands of the Chinese children that made this card.", "CAHxmas"));
            localData.answers.Add(new mod_xyzzy_card("Taking down Santa with a surface-to-air missile.", "CAHxmas"));
            localData.answers.Add(new mod_xyzzy_card("Socks.", "CAHxmas"));
            localData.answers.Add(new mod_xyzzy_card("Pretending to be happy.", "CAHxmas"));
            localData.answers.Add(new mod_xyzzy_card("Krampus, the Austrian Christmas monster.", "CAHxmas"));
            localData.answers.Add(new mod_xyzzy_card("The Star Wars Holiday Special.", "CAHxmas"));
            localData.answers.Add(new mod_xyzzy_card("My hot cousin.", "CAHxmas"));
            localData.answers.Add(new mod_xyzzy_card("Mall Santa.", "CAHxmas"));
            localData.answers.Add(new mod_xyzzy_card("Several intertwining love stories featuring Hugh Grant.", "CAHxmas"));
            localData.answers.Add(new mod_xyzzy_card("A Hungry-Man&trade; Frozen Christmas Dinner for one.", "CAHxmas"));
            localData.answers.Add(new mod_xyzzy_card("Gift-wrapping a live hamster.", "CAHxmas"));
            localData.answers.Add(new mod_xyzzy_card("Space Jam on VHS.", "CAHxmas"));
            localData.answers.Add(new mod_xyzzy_card("Immaculate conception.", "CAHxmas"));
            localData.answers.Add(new mod_xyzzy_card("Fucking up 'Silent Night' in front of 300 parents.", "CAHxmas"));
            localData.answers.Add(new mod_xyzzy_card("A visually arresting turtleneck.", "CAHxmas"));
            localData.answers.Add(new mod_xyzzy_card("A toxic family environment.", "CAHxmas"));
            localData.answers.Add(new mod_xyzzy_card("Eating an entire snowman.", "CAHxmas"));
            localData.answers.Add(new mod_xyzzy_card("Bumpses.", "NEIndy"));
            localData.answers.Add(new mod_xyzzy_card("A Vin Gerard H8 X 10.", "NEIndy"));
            localData.questions.Add(new mod_xyzzy_card("We got the third rope, now where's the fourth?", "NEIndy", 1));
            localData.answers.Add(new mod_xyzzy_card("Harry Acropolis.", "NEIndy"));
            localData.answers.Add(new mod_xyzzy_card("Under the ring.", "NEIndy"));
            localData.questions.Add(new mod_xyzzy_card("Tonights main event, _ vs. _.", "NEIndy", 2));
            localData.answers.Add(new mod_xyzzy_card("Afa The Wild Samoan.", "NEIndy"));
            localData.questions.Add(new mod_xyzzy_card("Tackle, Dropdown, _.", "NEIndy", 1));
            localData.answers.Add(new mod_xyzzy_card("Peanut Butter and Baby sandwiches.", "NEIndy"));
            localData.questions.Add(new mod_xyzzy_card("Christopher Daniels is late on his _.", "NEIndy", 1));
            localData.answers.Add(new mod_xyzzy_card("Yard Tards.", "NEIndy"));
            localData.answers.Add(new mod_xyzzy_card("Two girls, one cup.", "NEIndy"));
            localData.answers.Add(new mod_xyzzy_card("Ugly Mexican Hookers.", "NEIndy"));
            localData.answers.Add(new mod_xyzzy_card("Duct tape.", "NEIndy"));
            localData.answers.Add(new mod_xyzzy_card("Sodaj.", "NEIndy"));
            localData.questions.Add(new mod_xyzzy_card("Instead of booking _, they should have booked _.", "NEIndy", 2));
            localData.questions.Add(new mod_xyzzy_card("Genius is 10% inspiration, 90% _.", "NEIndy", 1));
            localData.questions.Add(new mod_xyzzy_card("They found _ in the dumpster behind _.", "NEIndy", 2));
            localData.answers.Add(new mod_xyzzy_card("Steve The Teacher.", "NEIndy"));
            localData.questions.Add(new mod_xyzzy_card("The best thing I ever got for Christmas was _.", "NEIndy", 1));
            localData.answers.Add(new mod_xyzzy_card("Jefferee.", "NEIndy"));
            localData.questions.Add(new mod_xyzzy_card("There's no crying in _.", "NEIndy", 1));
            localData.questions.Add(new mod_xyzzy_card("Mastodon! Pterodactyl! Triceratops! Sabretooth Tiger! _!", "NEIndy", 1));
            localData.answers.Add(new mod_xyzzy_card("Autoerotic Asphyxiation.", "NEIndy"));
            localData.questions.Add(new mod_xyzzy_card("Don't eat the _.", "NEIndy", 1));
            localData.answers.Add(new mod_xyzzy_card("Sonic The Hedgehog.", "NEIndy"));
            localData.answers.Add(new mod_xyzzy_card("Lotto Money.", "NEIndy"));
            localData.questions.Add(new mod_xyzzy_card("He did _ with the _!?!", "NEIndy", 2));
            localData.answers.Add(new mod_xyzzy_card("Jailbait.", "NEIndy"));
            localData.answers.Add(new mod_xyzzy_card("Prison rape.", "NEIndy"));
            localData.questions.Add(new mod_xyzzy_card("SOOOOO hot, want to touch the _.", "NEIndy", 1));
            localData.questions.Add(new mod_xyzzy_card("Stop looking at me _!", "NEIndy", 1));
            localData.answers.Add(new mod_xyzzy_card("Two And A Half Men.", "NEIndy"));
            localData.answers.Add(new mod_xyzzy_card("Anne Frank.", "NEIndy"));
            localData.answers.Add(new mod_xyzzy_card("Black Santa.", "NEIndy"));
            localData.questions.Add(new mod_xyzzy_card("I'm cuckoo for _ puffs.", "NEIndy", 1));
            localData.questions.Add(new mod_xyzzy_card("Silly rabbit, _ are for kids.", "NEIndy", 1));
            localData.answers.Add(new mod_xyzzy_card("Jesus Christ (our lord and saviour).", "NEIndy"));
            localData.answers.Add(new mod_xyzzy_card("Farting with your armpits.", "NEIndy"));
            localData.answers.Add(new mod_xyzzy_card("Poopsicles.", "NEIndy"));
            localData.answers.Add(new mod_xyzzy_card("Slaughtering innocent children.", "NEIndy"));
            localData.answers.Add(new mod_xyzzy_card("Sex with vegetables.", "NEIndy"));
            localData.answers.Add(new mod_xyzzy_card("My gay ex-husband.", "NEIndy"));
            localData.answers.Add(new mod_xyzzy_card("Accidentally sexting your mom.", "NEIndy"));
            localData.answers.Add(new mod_xyzzy_card("Tabasco in your pee-hole.", "NEIndy"));
            localData.answers.Add(new mod_xyzzy_card("Pee Wee Herman.", "NEIndy"));
            localData.answers.Add(new mod_xyzzy_card("A breath of fresh air.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("A great big floppy donkey dick.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("A pyramid scheme.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("A school bus surrounded by cop cars.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("A short walk in the desert with shovels.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("All the boys staring at your chest.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("An amorous stallion.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Being so wet it just slides out of you.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Being tarred and feathered.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Catching 'em all.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Chained to the bed and whipped to orgasmic bliss by a leather-clad woman.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Child-bearing hips.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Defenestration.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Dungeons and/or dragons.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Ecco the Dolphin.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("George Washington riding on a giant eagle.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Getting abducted and probed by aliens.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Going viral on YouTube.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Gushing.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Making the baby Jesus cry.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("More than you can chew.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Napalm.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Pancake bitches.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Playing God with the power of lightning.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Playing tonsil-hockey.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Racing cheese wheels downhill.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Riding the bomb.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Settling arguments with dance-offs.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Sheer spite.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Sinister laughter.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("SS Girls.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Stealing your sister's underwear.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Stroking a cat the wrong way.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Sucking and blowing.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("The bullet with your name on it.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("The entire rest of eternity, spent in fucking Bruges.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("The oceans rising to reclaim the land.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("A cocained-fuelled sex orgy heart attack.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("A cocktail umbrella ", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("A murder/suicide pact.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("A squirming mass of kittens.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("An angry mob with torches and pitchforks.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Biting my girlfriend like a vampire during sex.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Dropping your pants and saluting.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Frankenstein's Monster", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Getting a blowjob in a theater.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Going full retard.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Going slob-slob-slob all over that knob.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Leaking implants.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Low-flying planes.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Monkies flinging their own shit.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("My robot duplicate.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Other people’s children.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("People who can't take a joke. Seriously.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Popping a boner during Sex Ed class.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Projectile vomiting.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Pulling down panties with your teeth.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Saying ", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Shedding skin like a snake.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Shooting Valley Girls for like, saying like all the time. Really.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Slow seductive tentacle rape.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Talking like a pirate, y’arr!", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Tenderly kissing a unicorn's horn.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("That bastard Jesus!", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("The last shreads of dignity.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("The power of friendship.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("This card intentionally left blank.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Throwing water on a braless woman in a white t-shirt", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Upskirts.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Wasting all your money on hookers and booze.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Winning.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("A foot fetish.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("A powerful gag reflex.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("A tight, Asian pussy.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Explosive decompression.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Extraordinary Rendition.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Forgetting the safety word.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Greeting Christmas carollers naked.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Handcuffs, without the key.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Having a drill for a penis.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Hot Jailbait Ass.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Liposuction gone horrible wrong.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("My harem of scantily clad women.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Nazi Zombie Robot Ninjas.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Redneck gypsies.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Scissoring.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("A guy and two robots who won’t shut up.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("A shotgun wedding.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Anne Frank's diary", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Autoerotic asphyxiation.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Blow Up Bianca the Latex Lovedoll.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Endlessly tumbling down an up escalator.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Fun with nuns.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Getting it all over the walls.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Holiday Dinner by Jack Daniels.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Nailgun fights.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Teaching the bitch a lesson.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Nazi super science.", "NSFH"));
            localData.answers.Add(new mod_xyzzy_card("Making a human centipede.", "NSFH"));
            localData.questions.Add(new mod_xyzzy_card("Between love and madness lies _.", "NSFH", 1));
            localData.questions.Add(new mod_xyzzy_card("Instead of chess, the Grim Reaper now gambles for your soul with a game of _.", "NSFH", 1));
            localData.questions.Add(new mod_xyzzy_card("My father gave his life fighting to protect _ from _.", "NSFH", 2));
            localData.questions.Add(new mod_xyzzy_card("Why is my throat sore?", "NSFH", 1));
            localData.questions.Add(new mod_xyzzy_card("_ sparked a city-wide riot that only ended with _.", "NSFH", 2));
            localData.questions.Add(new mod_xyzzy_card("I’m very sorry Mrs. Smith, but Little Billy has tested positive for _.", "NSFH", 1));
            localData.questions.Add(new mod_xyzzy_card("Instead of beating them, Chris Brown now does _ to women.", "NSFH", 1));
            localData.questions.Add(new mod_xyzzy_card("Instead of cutting, trendy young emo girls now engage in _.", "NSFH", 1));
            localData.questions.Add(new mod_xyzzy_card("The definition of rock bottom is gambling away _.", "NSFH", 1));
            localData.questions.Add(new mod_xyzzy_card("The Mayan prophecies really heralded the coming of _ in 2012.", "NSFH", 1));
            localData.questions.Add(new mod_xyzzy_card("The next US election will be fought on the key issues of _ against _.", "NSFH", 2));
            localData.questions.Add(new mod_xyzzy_card("When I was 10 I wrote to Santa wishing for _.", "NSFH", 1));
            localData.questions.Add(new mod_xyzzy_card("Where or How I met my last signifigant other: _.", "NSFH", 1));
            localData.questions.Add(new mod_xyzzy_card("_, Never leave home without it.", "NSFH", 1));
            localData.questions.Add(new mod_xyzzy_card("_. This is my fetish.", "NSFH", 1));
            localData.questions.Add(new mod_xyzzy_card("David Icke's newest conspiracy theory states that _ caused _.", "NSFH", 2));
            localData.questions.Add(new mod_xyzzy_card("I did _ so you don't have to!", "NSFH", 1));
            localData.questions.Add(new mod_xyzzy_card("I need your clothes, your bike, and _.", "NSFH", 1));
            localData.questions.Add(new mod_xyzzy_card("In a new Cold War retro movie, the red menace tries to conquer the world through the cunning use of _.", "NSFH", 1));
            localData.questions.Add(new mod_xyzzy_card("In college, our lecturer made us write a report comparing _ to _.", "NSFH", 2));
            localData.questions.Add(new mod_xyzzy_card("In The Hangover part 3, those four guys have to deal with _, _, and _.", "NSFH", 3));
            localData.questions.Add(new mod_xyzzy_card("My zombie survival kit includes food, water, and _.", "NSFH", 1));
            localData.questions.Add(new mod_xyzzy_card("The way to a man's heart is through _.", "NSFH", 1));
            localData.questions.Add(new mod_xyzzy_card("What was the theme of my second wedding?", "NSFH", 1));
            localData.questions.Add(new mod_xyzzy_card("What's the newest Japanese craze to head West?", "NSFH", 1));
            localData.questions.Add(new mod_xyzzy_card("Everybody loves _.", "NSFH", 1));
            localData.questions.Add(new mod_xyzzy_card("I can only express myself through _.", "NSFH", 1));
            localData.questions.Add(new mod_xyzzy_card("My new porn DVD was completely ruined by the inclusion of _", "NSFH", 1));
            localData.questions.Add(new mod_xyzzy_card("My three wishes will be for _, _, and _.", "NSFH", 3));
            localData.questions.Add(new mod_xyzzy_card("The latest horrifying school shooting was inspired by _.", "NSFH", 1));
            localData.questions.Add(new mod_xyzzy_card("I got fired because of my not-so-secret obsession over _.", "NSFH", 1));
            localData.questions.Add(new mod_xyzzy_card("My new favourite sexual position is _", "NSFH", 1));
            localData.answers.Add(new mod_xyzzy_card("The primal, ball-slapping sex your parents are having right now.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("A cat video so cute that your eyes roll back and your spine slides out of your anus.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Cock.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("A cop who is also a dog.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Dying alone and in pain.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Gay aliens.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("The way white people is.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Reverse cowgirl.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("The Quesadilla Explosion Salad&trade; from Chili's&copy;.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Actually getting shot, for real.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Not having sex.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Vietnam flashbacks.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Running naked through a mall, pissing and shitting everywhere.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Nothing.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Warm, velvety muppet sex.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Self-flagellation.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("The systematic destruction of an entire people and their way of life.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Samuel L. Jackson.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("A boo-boo.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Going around punching people.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("The entire Internet.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Some kind of bird-man.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Chugging a lava lamp.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Having sex on top of a pizza.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Indescribable loneliness.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("An ass disaster.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Shutting the fuck up.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("All my friends dying.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Putting an entire peanut butter and jelly sandwich into the VCR.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Spending lots of money.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Some douche with an acoustic guitar.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Flying robots that kill people.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("A greased-up Matthew McConaughey.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("An unstoppable wave of fire ants.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Not contributing to society in any meaningful way.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("An all-midget production of Shakespeare's <i>Richard III</i>.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Screaming like a maniac.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("The moist, demanding chasm of his mouth.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Filling every orifice with butterscotch pudding.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Unlimited soup, salad, and breadsticks.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Crying into the pages of Sylvia Plath.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Velcro&trade;.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("A PowerPoint presentation.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("A surprising amount of hair.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Eating Tom Selleck's mustache to gain his powers.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Roland the Farter, flatulist to the king.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("That ass.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("A pile of squirming bodies.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Buying the right pants to be cool.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Blood farts.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Three months in the hole.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("A botched circumcision.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("The Land of Chocolate.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Slapping a racist old lady.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("A lamprey swimming up the toilet and latching onto your taint.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Jumping out at people.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("A black male in his early 20s, last seen wearing a hoodie.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Mufasa's death scene.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Bill Clinton, naked on a bearskin rug with a saxophone.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Demonic possession.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("The Harlem Globetrotters.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Vomiting mid-blowjob.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("My manservant, Claude.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Having shotguns for legs.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Letting everyone down.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("A spontaneous conga line.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("A vagina that leads to another dimension.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Disco fever.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Getting your dick stuck in a Chinese finger trap with another dick.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Fisting.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("The thin veneer of situational causality that underlies porn.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Girls that always be textin'.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Blowing some dudes in an alley.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Drinking ten 5-hour ENERGYs&reg; to get fifty continuous hours of energy.", "CAHe3"));
            localData.answers.Add(new mod_xyzzy_card("Sneezing, farting, and coming at the same time.", "CAHe3"));
            localData.questions.Add(new mod_xyzzy_card("A successful job interview begins with a firm handshake and ends with _.", "CAHe3", 1));
            localData.questions.Add(new mod_xyzzy_card("Lovin' you is easy 'cause you're _.", "CAHe3", 1));
            localData.questions.Add(new mod_xyzzy_card("My life is ruled by a vicious cycle of _ and _.", "CAHe3", 2));
            localData.questions.Add(new mod_xyzzy_card("The blind date was going horribly until we discovered our shared interest in _.", "CAHe3", 1));
            localData.questions.Add(new mod_xyzzy_card("_. Awesome in theory, kind of a mess in practice.", "CAHe3", 1));
            localData.questions.Add(new mod_xyzzy_card("I'm not like the rest of you. I'm too rich and busy for _.", "CAHe3", 1));
            localData.questions.Add(new mod_xyzzy_card("In the seventh circle of Hell, sinners must endure _ for all eternity.", "CAHe3", 1));
            localData.questions.Add(new mod_xyzzy_card("_: Hours of fun. Easy to use. Perfect for _!", "CAHe3", 2));
            localData.questions.Add(new mod_xyzzy_card("What left this stain on my couch?", "CAHe3", 1));
            localData.questions.Add(new mod_xyzzy_card("Call the law offices of Goldstein & Goldstein, because no one should have to tolerate _ in the workplace.", "CAHe3", 1));
            localData.questions.Add(new mod_xyzzy_card("When you get right down to it, _ is just _.", "CAHe3", 2));
            localData.questions.Add(new mod_xyzzy_card("Turns out that _-Man was neither the hero we needed nor wanted.", "CAHe3", 1));
            localData.questions.Add(new mod_xyzzy_card("As part of his daily regimen, Anderson Cooper sets aside 15 minutes for _.", "CAHe3", 1));
            localData.questions.Add(new mod_xyzzy_card("Money can't buy me love, but it can buy me _.", "CAHe3", 1));
            localData.questions.Add(new mod_xyzzy_card("With enough time and pressure, _ will turn into _.", "CAHe3", 2));
            localData.questions.Add(new mod_xyzzy_card("And what did you bring for show and tell?", "CAHe3", 1));
            localData.questions.Add(new mod_xyzzy_card("During high school, I never really fit in until I found _ club.", "CAHe3", 1));
            localData.questions.Add(new mod_xyzzy_card("Hey, baby, come back to my place and I'll show you _.", "CAHe3", 1));
            localData.questions.Add(new mod_xyzzy_card("After months of practice with _, I think I'm finally ready for _.", "CAHe3", 2));
            localData.questions.Add(new mod_xyzzy_card("To prepare for his upcoming role, Daniel Day-Lewis immersed himself in the world of _.", "CAHe3", 1));
            localData.questions.Add(new mod_xyzzy_card("Finally! A service that delivers _ right to your door.", "CAHe3", 1));
            localData.questions.Add(new mod_xyzzy_card("My gym teacher got fired for adding _ to the obstacle course.", "CAHe3", 1));
            localData.questions.Add(new mod_xyzzy_card("Having problems with _? Try _!", "CAHe3", 2));
            localData.questions.Add(new mod_xyzzy_card("As part of his contract, Prince won't perform without _ in his dressing room.", "CAHe3", 1));
            localData.questions.Add(new mod_xyzzy_card("Listen, son. If you want to get involved with _, I won't stop you. Just steer clear of _.", "CAHe3", 2));
            localData.answers.Add(new mod_xyzzy_card("A freshly-filled diaper", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("Glue", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("An unusually-attractive transvestite", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("Hand-me-down adult diapers", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("A stillborn fetus", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("A disgraced pelican", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("Three buckets of urine, free for 2 nights, with no late fee", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("My testicles", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("A black woman's vagina", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("My asshole", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("A whale's blowhole", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("2 Girls 1 Cup", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("The Big Bang Theory (TV)", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("Teen pregnancy", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("Ass hair", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("Vaginal warts", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("Ellen Degeneres", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("Jews Against Humanity", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("Indy wrestling", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("Cunt", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("Beating a crowd of delightful parents to death with a steel dildo", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("Beating a crowd of delightful parents to death with a steel dildo while dressed as Ru Paul's brother, Ron.", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("A roll in the hay", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("God Hates You", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("Manboobs.", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("Daniel Benoit", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("Vomiting in the shower", "Image1"));
            localData.questions.Add(new mod_xyzzy_card("I just met you and this is crazy, but here's _, so _ maybe", " Image1", 2));
            localData.questions.Add(new mod_xyzzy_card("It's only _ if you get caught!", " Image1", 1));
            localData.questions.Add(new mod_xyzzy_card("_: The Next Generation", " Image1", 1));
            localData.questions.Add(new mod_xyzzy_card("Terminator 4: _", " Image1", 1));
            localData.questions.Add(new mod_xyzzy_card("Disney presents _ on ice!", " Image1", 1));
            localData.questions.Add(new mod_xyzzy_card("_. The other white meat.", " Image1", 1));
            localData.questions.Add(new mod_xyzzy_card("A _ a day keeps the _ away.", " Image1", 2));
            localData.answers.Add(new mod_xyzzy_card("An intellectually superior overlord", "Image1"));
            localData.questions.Add(new mod_xyzzy_card("I'm sweating like a _ at a _.", " Image1", 2));
            localData.questions.Add(new mod_xyzzy_card("I love the smell of _ in the morning.", " Image1", 1));
            localData.questions.Add(new mod_xyzzy_card("You're not gonna believe this, but _.", " Image1", 1));
            localData.answers.Add(new mod_xyzzy_card("Dwight Schrute", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("Casey Anthony", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("Clubbin seals", "Image1"));
            localData.answers.Add(new mod_xyzzy_card("Stunt cock", "Image1"));
            localData.questions.Add(new mod_xyzzy_card("_. All the cool kids are doing it.", " Image1", 1));
            localData.answers.Add(new mod_xyzzy_card("Anal lice", "Image1"));
            localData.questions.Add(new mod_xyzzy_card("So I was _ in my cubicle at work, and suddenly _!", " Image1", 2));
            localData.answers.Add(new mod_xyzzy_card("Lightsaber Dildos", "Image1"));
            localData.questions.Add(new mod_xyzzy_card("Baskin Robbins just added a 32nd flavor: _!", " Image1", 1));
            localData.questions.Add(new mod_xyzzy_card("I can drive and _ at the same time.", " Image1", 1));
            localData.questions.Add(new mod_xyzzy_card("_ ain't nothin' to fuck wit'!", " Image1", 1));
            localData.answers.Add(new mod_xyzzy_card("Jaime Lannister, 'The Kingslayer'", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Cersei Lannister, the Queen", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Joffrey Baratheon, the Prince", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Tyrion Lannister, 'The Imp'", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Ned Stark, Lord of Winterfell, Warden of the North", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Robb Stark, heir apparent of Winterfell", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Jon Snow, the bastard", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Catelyn Stark, Lady of Winterfell", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Sansa Stark, betrothed to Prince Joffrey", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Arya Stark", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Bran Stark", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Hodor", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("The Wall", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("The Night's Watch", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Danerys Targaryen, Khaleesi of the Dothraki", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Theon Greyjoy, Ned Stark's youthful ward", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Peter 'Littlefinger' Baelish", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Lord Varys, the Spider", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("King Robert Baratheon, First of His Name, King of the Andals and the First Men, Lord Protector of the Realm", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Khal Drogo, Dothraki horse lord", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("The Iron Throne", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("HODOR!!", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Ros, the red-headed whore", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Winterfell", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Kings's Landing", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("The North", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Beyond the Wall", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Westeros", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("The Seven Kingdoms", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Direwolves", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("White Walkers", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Dragons", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("'Winter is Coming'", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("The old gods and the new", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Incest, hot twin on twin action", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("House Stark", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("House Lannister", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("House Targaryen", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("George R. R. Martin", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Gratuitous nudity, the way only HBO® can provide", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Throwing a boy out of a window to cover up incest", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Joining the Night's Watch", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Selling your sister to Dothraki nomads", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Making your husband love you through cunning use of reverse cowgirl", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Running a whorehouse, which is better than owning ships", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Conquering the continent with dragons", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Being forced to marry an abusive king", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Beheading a man for having no honor", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Explaining complicated plot with lots of naked women around", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Trusting Littlefinger", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Learning the prince is a bastard and the product of incest", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Slapping Joffrey. Repeatedly.", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Cutting off your enemies' heads and mounting them on spikes", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Raising your husband's bastard son as your own", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Asking a teenage girl if she's 'bled yet'", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Making millions of fans cry by killing off beloved characters", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Hodoring", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Riding off to join your best friend's rebellion", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Breastfeeding your creepy son until he's 9 years old", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Having a giant wolf for a pet", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Beheaded on the steps of the Sept of Baelor", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Killed by a member of the Kingsguard", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Seized the Iron Throne by any means necessary", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Built a 700 foot high wall to keep out bad things", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Born a bastard", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Butchered by White Walkers and arranged in an artful pattern", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Appointed as Hand of the King", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Found enough wolf cubs for all the children", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Fondled by your brother on your wedding day", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Climbed the wrong wall at the wrong time", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Started a pointless vendetta with another House", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Increased ratings with the use of gratuitous nudity", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Carried by Hodor", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Pissed off of the Wall just because", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Swore an oath to the old gods and the new", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Brought home a new baby bastard for your wife to hate", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Negotiated a wedding no one will like", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Rode a dragon, like a boss", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Changed things from the book, infuriating fans", "GOT"));
            localData.answers.Add(new mod_xyzzy_card("Spent an entire reign chasing boars and fucking whores", "GOT"));
            localData.questions.Add(new mod_xyzzy_card("If Ned Stark had _, he never would have _.", "GOT", 2));
            localData.questions.Add(new mod_xyzzy_card("Brace yourselves, _ is coming.", "GOT", 1));
            localData.questions.Add(new mod_xyzzy_card("In exchange for his sister, Viserys was given _.", "GOT", 1));
            localData.questions.Add(new mod_xyzzy_card("Despite his best efforts, King Robert filled his reign with _.", "GOT", 1));
            localData.questions.Add(new mod_xyzzy_card("_ was proclaimed the true king of the Seven Kingdoms.", "GOT", 1));
            localData.questions.Add(new mod_xyzzy_card("In _, you win or you lose.", "GOT", 1));
            localData.questions.Add(new mod_xyzzy_card("Because of _, Danerys was called _ by everyone.", "GOT", 2));
            localData.questions.Add(new mod_xyzzy_card("I will take what is mine with _ and _.", "GOT", 2));
            localData.questions.Add(new mod_xyzzy_card("There is no word for _ in Dothraki.", "GOT", 1));
            localData.questions.Add(new mod_xyzzy_card("In the next Game of Thrones book, George R. R. Martin said _ will _.", "GOT", 2));
            localData.questions.Add(new mod_xyzzy_card("All hail _! King of _!", "GOT", 2));
            localData.questions.Add(new mod_xyzzy_card("A Lannister always pays _.", "GOT", 1));
            localData.questions.Add(new mod_xyzzy_card("First lesson, stick them with _.", "GOT", 1));
            localData.questions.Add(new mod_xyzzy_card("In the name of _, first of his _.", "GOT", 2));
            localData.questions.Add(new mod_xyzzy_card("The things I do for _.", "GOT", 1));
            localData.questions.Add(new mod_xyzzy_card("Hodor only ever says _.", "GOT", 1));
            localData.questions.Add(new mod_xyzzy_card("The next Game of Thrones book will be titled _ of _.", "GOT", 2));
            localData.questions.Add(new mod_xyzzy_card("A Dothraki wedding without _ is considered a dull affair.", "GOT", 1));
            localData.questions.Add(new mod_xyzzy_card("After I was caught _, I was forced to join the Night's Watch.", "GOT", 1));
            localData.questions.Add(new mod_xyzzy_card("A man without _ is a man without power.", "GOT", 1));
            localData.answers.Add(new mod_xyzzy_card("Full HD.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("The Gravity Gun.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("Reading the comments.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("70,000 gamers sweating and farting inside an airtight steel dome.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("Allowing nacho cheese to curdle in your beard while you creep in League of Legends.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("Achieving the manual dexterity and tactical brilliance of a 12-year-old Korean boy.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("Rolling a D20 to save a failing marriage.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("The collective wail of every Magic player suddenly realizing that they've spent hundreds of dollars on pieces of cardboard.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("Being an attractive elf trapped in an unattractive human's body.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("Temporary invincibility.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("The Sarlacc.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("Filling every pouch of a UtiliKilt&trade; with pizza.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("Bowser's aching heart.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("Mario Kart rage.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("Nude-Modding Super Mario World.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("An angry stone head that stomps on the floor every three seconds.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("Yoshi's huge egg-laying cloaca.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("The Cock Ring of Alacrity.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("Offering sexual favors for an ore and a sheep.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("A home-made, cum-stained Star Trek uniform.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("Unlocking a new sex position.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("The boner hatch in the Iron Man suit.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("Never watching, discussing, or thinking about My Little Pony.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("Turn-of-the-century sky racists.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("The decade of legal inquests following a single hour of Grand Theft Auto.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("A fully-dressed female video game character.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("Buying virtual clothes for a Sim family instead of real clothes for a real family.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("Google Glass + e-Cigarette: Ultimate Combo!", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("Tapping Serra Angel.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("Charles Barkley Shut Up and Jam!", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("Legendary Creature - Robert Khoo.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("Winning the approval of Cooking Mama that you never got from actual mama.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("Eating a pizza that's lying in the street to gain health.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("Getting into a situation with an Owlbear.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("Grand Theft Auto: Fort Lauderdale.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("A madman who lives in a police box and kidnaps women.", "PAXP13"));
            localData.answers.Add(new mod_xyzzy_card("SNES cartridge cleaning fluid.", "PAXP13"));
            localData.questions.Add(new mod_xyzzy_card("The most controversial game at PAX this year is an 8-bit indie platformer about _.", "PAXP13", 1));
            localData.questions.Add(new mod_xyzzy_card("What made Spock cry?", "PAXP13", 1));
            localData.questions.Add(new mod_xyzzy_card("_: Achievement unlocked.", "PAXP13", 1));
            localData.questions.Add(new mod_xyzzy_card("There was a riot at the Gearbox panel when they gave the attendees _.", "PAXP13", 1));
            localData.questions.Add(new mod_xyzzy_card("In the new DLC for Mass Effect, Shepard must save the galaxy from _.", "PAXP13", 1));
            localData.questions.Add(new mod_xyzzy_card("What's the latest bullshit that's troubling this quaint fantasy town?", "PAXP13", 1));
            localData.questions.Add(new mod_xyzzy_card("No Enforcer wants to manage the panel on _.", "PAXP13", 1));
            localData.answers.Add(new mod_xyzzy_card("An immediately regrettable $9 hot dog from the Boston Convention Center.", "PAXE13"));
            localData.answers.Add(new mod_xyzzy_card("Running out of stamina.", "PAXE13"));
            localData.answers.Add(new mod_xyzzy_card("Casting Magic Missile at a bully.", "PAXE13"));
            localData.answers.Add(new mod_xyzzy_card("Getting bitch slapped by Dhalsim.", "PAXE13"));
            localData.answers.Add(new mod_xyzzy_card("Firefly: Season 2.", "PAXE13"));
            localData.answers.Add(new mod_xyzzy_card("Rotating shapes in mid-air so that they fit into other shapes when they fall.", "PAXE13"));
            localData.answers.Add(new mod_xyzzy_card("Jiggle physics.", "PAXE13"));
            localData.answers.Add(new mod_xyzzy_card("Paying the iron price.", "PAXE13"));
            localData.answers.Add(new mod_xyzzy_card("Sharpening a foam broadsword on a foam whetstone.", "PAXE13"));
            localData.answers.Add(new mod_xyzzy_card("The rocket launcher.", "PAXE13"));
            localData.answers.Add(new mod_xyzzy_card("The depression that ensues after catching 'em all.", "PAXE13"));
            localData.answers.Add(new mod_xyzzy_card("Loading from a previous save.", "PAXE13"));
            localData.answers.Add(new mod_xyzzy_card("Violating the first Law of Robotics.", "PAXE13"));
            localData.answers.Add(new mod_xyzzy_card("Getting inside the Horadic Cube with a hot babe and pressing the transmute button.", "PAXE13"));
            localData.answers.Add(new mod_xyzzy_card("Punching a tree to gather wood.", "PAXE13"));
            localData.answers.Add(new mod_xyzzy_card("Spending the year's insulin budget on Warhammer 40k figurines.", "PAXE13"));
            localData.answers.Add(new mod_xyzzy_card("The Klobb.", "PAXE13"));
            localData.answers.Add(new mod_xyzzy_card("Achieving 500 actions per minute.", "PAXE13"));
            localData.answers.Add(new mod_xyzzy_card("Vespene gas.", "PAXE13"));
            localData.answers.Add(new mod_xyzzy_card("Wil Wheaton crashing an actual spaceship.", "PAXE13"));
            localData.answers.Add(new mod_xyzzy_card("Charging up all the way.", "PAXE13"));
            localData.answers.Add(new mod_xyzzy_card("Judging elves by the color of their skin and not the content of their character.", "PAXE13"));
            localData.answers.Add(new mod_xyzzy_card("Smashing all the pottery in a Pottery Barn in search of rupees.", "PAXE13"));
            localData.answers.Add(new mod_xyzzy_card("Forgetting to eat and consequently dying.", "PAXE13"));
            localData.questions.Add(new mod_xyzzy_card("I have an idea even better than Kickstarter, and it's called _starter", "PAXE13", 1));
            localData.questions.Add(new mod_xyzzy_card("You have been waylaid by _ and must defend yourself.", "PAXE13", 1));
            localData.questions.Add(new mod_xyzzy_card("In the final round of this year's Omegathon, Omeganauts must face off in a game of _.", "PAXE13", 1));
            localData.questions.Add(new mod_xyzzy_card("Action stations! Action stations! Set condition one throughout the fleet and brace for _!", "PAXE13", 1));
            localData.questions.Add(new mod_xyzzy_card("Press &darr;&darr;&larr;&rarr; to unleash _.", "PAXE13", 1));
            localData.questions.Add(new mod_xyzzy_card("I don't know exactly how I got the PAX plague, but I suspect it had something to do with _.", "PAXE13", 1));
            localData.answers.Add(new mod_xyzzy_card("Zero F**K's Given!", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Windows update", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Wilfrord Brimley's Mustache", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Wikileaks", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Why not Zoidberg?!", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("White Shirt", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Warp core breach", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("W.O.P.R.", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Vinyl Vanna", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Vegas 2.0", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Using 4Chan for parenting advice", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("User Error", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Undercover NBC DateLine Reporter", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("UDP Handshake", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Two Girls One Cup", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Truffle Shuffle", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Trigger word", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Tractor Beam", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Toxic BBQ", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("I'm a text", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Tongue punch that fart box, boy", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("TL;DR", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Threat modeling", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Throat Punching", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("There's talks at DEF CON?", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("The Spanish Inquisition", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("The smell of glitter and lost dreams", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("The plan was to crowd source a plan", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("The fractured elements of her psyche reassembled themselves into an exact likeness of a snarling ferret and she self-destructed", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("The asshole sitting to my right.", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("The asshole sitting to my left", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("That's What ~ She", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("That's Racist!", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("That place where I put that thing that time.", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("That just happened and we let that happen.", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Tentacle Porn", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("TARDIS", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Sweat, anger and shame", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Stolen laptops", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Sticky keyboard", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Steve Wozniak", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Steampunk", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("SRDF (Self Righteous Dick Face)", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Squirrel", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Spyware", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Spotting a FED", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("SPAM with Bacon", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Something something danger zone. I know. I'm not even trying anymore.", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Spacedicks", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Slow Clap", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Six gummy bears and some scotch", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Situational awareness", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Shut up and take my money", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Schrödinger's cat", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Shenanigans", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Security theater", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Security Evangelist", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Security by obscurity", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Script kiddies", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Said no one ever!", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Sabu", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Running backwards through a corn field", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Rule 34", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Ruby on Rails", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Rolling Natural 20's", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("ROFLCOPTER", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Riding a horse", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Ridiculously Photogenic Guy", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Ribbed for their pleasure", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Restore from backups", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Redbull without a cause", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Red Shirts", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("ReCaptcha", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Real men of Genius", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Rainbow tables", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Rageface", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Rage quit", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Put Kevin back", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Put a bird on it", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Purchasing challenge coins on eBay", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Prism", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Priest in a thong doing the Gangnam Style", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Pressing the red button", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Prenda Law", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Prairie dogging during an interview", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Practicing Gringo Warrior at home with baby oil. Naked.", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("P0rn", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("PORK CHOP SANDWICHES!", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Pop, Pop, Ret", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Pool2Girl", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Please do the needful", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Pirate Party", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Pepper spray", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("PedoBear", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Patrick Star", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Pastebin password files", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Passwords emailed in plain text", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Password: Guest", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("0wning You", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Online backups", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("One Salty Hash", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("OMGBTFBBQ", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Obvious", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("NSA", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Now I'm into something... Darker", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Not a single fuck was given", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("North Korea's Twitter Account", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("No Starch Press", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("No Reason", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Nmap", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Ninjas, Pirates, Robots, and Zombies!", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Ninja badge", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Nigerian scammers", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Neck beard", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("NAMBLA", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Na-ah-ah You didn't say the magic word!", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("My sex robot Fisto Roboto", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("My massive SSD", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("My little Bronies", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("My first Prostate Exam", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Mouth Hugs", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Mega", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Mega Upload", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Math is hard. Lets go shopping!", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Masturbating in a hot tub for a Ninja Badge", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Mansplaining", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Maniacally laughing while wearing a monocle", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Making a sandwich", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Maintaining the Ballmer Peak", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Lock picks", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Level 8 Portal", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Lemon Party", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Learning something at Con", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Lady boner", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("L0pht", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Keyloggers", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Kevin Mitnick", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Kegels", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Just the Tip", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Just a sniff", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Julian Assange", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("John McAfee", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("It's just a bunch of ones and zeros", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("It blended!", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Ingress", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Infected email attachments", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("In the cloud", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Implied Situational Consent", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Ill-tempered sea bass", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("If you know what I mean", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Identity theft", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("ICANN", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("I should buy a boat", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Humperdink award", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("HuBot (Chatroom bot)", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Hookers & Blow", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Hashtag", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Handcuffs", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Grumpy Cat", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Gray beard, gray balls", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Grammar Nazi", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Got it done!", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Good Guy Greg", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Golf cart", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Glasshole", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Getting thrown the the pool by the Goons with all your tech", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Getting hammered in the ass so much you die of getting hammered in the ass", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Getting F'd in the A with a D", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Getting a sympathy boner", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Fyodor", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("FX", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Forking someone's repo", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Forever Alone", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("FOIA Request", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Floppy Disk", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Flipping a table", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Flesh light", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Flame wars", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Fist full of assholes", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Fish fingers and custard", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("First World Problems", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Finished it last week!", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("FemiNazi's", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Fear Uncertainty Doubt (FUD)", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Fapping while wearing a horse head mask", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Fapping on the family computer", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Fapped", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Facepalm", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("EXIF data stalking", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("End User License Agreement", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Electronic Frontier Foundation", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Edward Snowden", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Drunken Muppet", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Drones", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Double ROT13", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Double Facepalm", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Don't Blink", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Dr. Who", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Dongs", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Doing the '(you are) NOT the father' dance", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Digital Millennium Copyright Act (DMCA)", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Die in a fire", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Dick and/or Balls", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("derp.rar (yo.zip)", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Def Con Wireless", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Do not connect to this!", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Deep C Phishing", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Dark Tangent", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Dan Kaminsky Password Generator", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Dan Kaminsky", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Daaaaaanger Zone!", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Cyber war", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Cyber-douchery", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Cyber Punk", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Crying over spilt milk", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Crash Override", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Copyright trolls", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Coding while listening to whale songs", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Clicking shit", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Chuck Norris", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("China", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Check a look at you later", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Cat memes", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Caressing a man's hairy chest", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Captain Crunch (John Draper)", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Butthurt", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Butt chugging mom's boxed wine", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("But then I'd have to kill you", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Big Dongles", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Big Data", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Being the big spoon", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Being the little spoon", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Being sexually aroused by the sight of TSA's gloves", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Bath salts", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Bacon", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Babe caught me slippin'", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Awkward mouth hugs", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Awkward hugs", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Asymmetric encryption", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Arbitrary code execution", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("APT1", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Anonymous", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("And then it died", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("And boom goes the dynamite", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("An arrow to the knee", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Altair 8800", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("All the Things!", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Alexis Park", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Ain't nobody got time for dat!", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("Ada Initiative approved flesh-light with anti-rape condom included!", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("ACTII pr0n", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("A van down by the river", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("A town with no ducks", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("A series of explicit Post-It notes", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("A series of tubes", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("A Raspberry Pi", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("A Payphone", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("A Ninja-tel Phone", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("A Hak5 Pineapple", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("A Googly eyed blow job", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("A giant cup of STFU", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("A fake ID made from Kinko's", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("A baby's arm holding an apple", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("A 'Pair of Docs'", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("64 Bit Keys", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("503 Card Unavailable", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("501 Card Error", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("500 internal card error", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("406 Not Allowed", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("404 Not Found", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("403 Forbidden", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("401 Unauthorized", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("3D printed P0rn", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("302 Card Redirect", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("10,000 Canadian Pennies", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("1.21 Jigawatts", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("(wub) (wub) (wub)", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("'I Survived Ada Camp' Challenge Coin", "HACK"));
            localData.answers.Add(new mod_xyzzy_card("1337 Sp3ak", "HACK"));
            localData.questions.Add(new mod_xyzzy_card("/r/ _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("The Ada Initiative is now attacking _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("Not another _ in the hotel elevator!", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("Closing Ceremonies drinking game: Every time _ is mentioned... DRINK!", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("In a Congressional hearing, US CYBERCOM commander Gen. Alexander claimed the latest data breach was due to _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("The Maker Faire was unexpectedly interrupted by _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("Do you even _?", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("Come to the dark side, we have _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("Y U NO _!!!!!", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("While alone in the server room I _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("When I get drunk I am an expert on _", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("Well, guess what? I’ve got a fever, and the only prescription is more _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("We should take _ and push it _.", "HACK", 2));
            localData.questions.Add(new mod_xyzzy_card("We decided to _ to raise money for the EFF.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("TSA wouldn't allow me through because of my _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("Tonight's Final Hacker Jeopardy category will be _!", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("Today's PaulDotCom podcast featured _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("These are not the _ you are looking for.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("The snozberries taste like _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("The only winning move is to _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("The next cyber war will feature _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("The best part of Alexis Park was all the _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("So long and thanks for all the _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("Security through obscurity is better than _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("Rule 34 _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("Rock, Paper, Scissors, Lizard, _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("Our most powerful weapon for the Zombie Apocalypse will be _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("Only half of programming is coding. The other 90% is _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("One does not simply _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("On the Internet, no one can tell you're _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("Occupy _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("Next year's scavenger hunt is rumored to include finding a _ with a _.", "HACK", 2));
            localData.questions.Add(new mod_xyzzy_card("Next time we meet we should _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("My extremely large _ is what makes me better than you.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("My _ brings all the _ to the yard.", "HACK", 2));
            localData.questions.Add(new mod_xyzzy_card("Most hackers smell like _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("Las Vegas is best known for _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("Keep calm and _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("It's dangerous to go alone. Take _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("It smells like _ in this room.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("In a shocking move Archive.org decided to NOT back up _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("I'mma let you finish but _ is the best _ of all time.", "HACK", 2));
            localData.questions.Add(new mod_xyzzy_card("I'm fucking tired of hearing about _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("I would be doing more with my life, except for this _ in the way.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("I work 80 hours a week and still can't afford a _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("I used to be a hacker like you, until I took a(n) _ to the knee.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("I use _ to secure all of my personal data.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("I spotted the fed and all I got was _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("I look like a geeky hacker, but I don't know anything about _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("I have the biggest _, ever!", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("I find your lack of _ disturbing.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("I can't believe they rejected my talk on _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("I can haz _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("HOLY _ BATMAN!!", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("High Tech start-up company combines _ with _.", "HACK", 2));
            localData.questions.Add(new mod_xyzzy_card("Go home _, you're drunk.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("Go Go Gadget _!", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("Drink all the _. Hack all the _.", "HACK", 2));
            localData.questions.Add(new mod_xyzzy_card("Def Con Kids will now focus on teaching young hackers _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("Confession Bear Says: _", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("But does _ run NetBSD?", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("Am I the only one around here who _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("All I did was _ but someone gave me a red card.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("35% of all hackers have to deal with _.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("_. There's an app for that.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("_. This is why I can't have nice things!", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("_: You keep using that term. I do not think it means what you think it means.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("_ is now outsourced to call centers in India.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("_ shot first.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("_ Killed the barrel roll", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("_ A'int Nobody Got Time For Dat!!", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("_ Put a bird on it!", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("_ makes me puke rainbows.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("_ is also monitored by Prism.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("_ is what keeps us together.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("_ is a better replacement for crypto.", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("_ riding a Segway", "HACK", 1));
            localData.questions.Add(new mod_xyzzy_card("One day, over my fireplace, I'm going to have a massive painting of _. You know, to remind me where I came from.", "HACK", 1));
            localData.answers.Add(new mod_xyzzy_card("10 Incredible Facts About the Anus.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("A Native American who solves crimes by going into the spirit world.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("A Ugandan warlord.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("A bunch of idiots playing a card game instead of interacting like normal humans.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("A dance move that's just sex.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("A fart.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("A for-real lizard that spits blood from its eyes.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("A gender identity that can only be conveyed through slam poetry.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("A hopeless amount of spiders.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("A horse with no legs.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("A kiss on the lips.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("A manhole.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("A sex comet from Neptune that plunges the Earth into eternal sexiness.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("A sex goblin with a carnival penis.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("A shiny rock that proves I love you.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Actual mutants with medical conditions and no superpowers.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Africa.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("All the single ladies.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Almost giving money to a homeless person.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Ambiguous sarcasm.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("An interracial handshake.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Angelheaded hipsters burning for the ancient heavenly connection to the starry dynamo in the machinery of night.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Ass to mouth.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Blackula.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Bouncing up and down.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Calculating every mannerism so as not to suggest homosexuality.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Child Protective Services.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Crazy opium eyes.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Dem titties.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Depression.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Doo-doo.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Drinking responsibly.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Exploding pigeons.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Falling into the toilet.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Finally finishing off the Indians.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Fucking a corpse back to life.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Grammar nazis who are also regular Nazis.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("How awesome I am.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Injecting speed into one arm and horse tranquilizer into the other.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Interspecies marriage.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Jizz.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Khakis.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Lots and lots of abortions.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Moderate-to-severe joint pain.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("My dad's dumb fucking face.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("My sex dungeon.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("My worthless son.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Neil Diamond's Greatest Hits.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("No clothes on, penis in vagina.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Party Mexicans.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Prince Ali, fabulous he, Ali Ababwa.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Sharks with legs.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Smoking crack, for instance.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Snorting coke off a clown's boner.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Some sort of Asian.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Sports.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Stuffing a child's face with Fun Dip® until he starts having fun.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Sugar madness.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("The complex geopolitical quagmire that is the Middle East.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("The euphoric rush of strangling a drifter.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("The peaceful and nonthreatening rise of China.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("The safe word.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("The secret formula for ultimate female satisfaction.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("The size of my penis.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("The tiniest shred of evidence that God is real.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Three consecutive seconds of happiness.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Unquestioning obedience.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("What Jesus would do.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Whatever a McRib® is made of.", "CAHe4"));
            localData.answers.Add(new mod_xyzzy_card("Whispering all sexy.", "CAHe4"));
            localData.questions.Add(new mod_xyzzy_card("2 AM in the city that never sleeps. The door swings open and she walks in, legs up to here. Something in her eyes tells me she's looking for _.", "CAHe4", 1));
            localData.questions.Add(new mod_xyzzy_card("Adventure. Romance. _. From Paramount Pictures, _.", "CAHe4", 2));
            localData.questions.Add(new mod_xyzzy_card("Alright, bros. Our frat house is condemned, and all the hot slampieces are over at Gamma Phi. The time has come to commence Operation _.", "CAHe4", 1));
            localData.questions.Add(new mod_xyzzy_card("As king, how will I keep the peasants in line?", "CAHe4", 1));
            localData.questions.Add(new mod_xyzzy_card("Dear Leader Kim Jong-un, our village praises your infinite wisdom with a humble offering of _.", "CAHe4", 1));
            localData.questions.Add(new mod_xyzzy_card("Do not fuck with me! I am literally _ right now.", "CAHe4", 1));
            localData.questions.Add(new mod_xyzzy_card("Every step towards _ gets me a little bit closer to _.", "CAHe4", 2));
            localData.questions.Add(new mod_xyzzy_card("Forget everything you know about _, because now we've supercharged it with _!", "CAHe4", 2));
            localData.questions.Add(new mod_xyzzy_card("Honey, I have a new role-play I want to try tonight! You can be _, and I'll be _.", "CAHe4", 2));
            localData.questions.Add(new mod_xyzzy_card("How am I compensating for my tiny penis?", "CAHe4", 1));
            localData.questions.Add(new mod_xyzzy_card("I am become _, destroyer of _!", "CAHe4", 2));
            localData.questions.Add(new mod_xyzzy_card("I'm pretty sure I'm high right now, because I'm absolutely mesmerized by _.", "CAHe4", 1));
            localData.questions.Add(new mod_xyzzy_card("I'm sorry sir, but we don't allow _ at the country club.", "CAHe4", 1));
            localData.questions.Add(new mod_xyzzy_card("If you can't handle _, you'd better stay away from _.", "CAHe4", 2));
            localData.questions.Add(new mod_xyzzy_card("In return for my soul, the Devil promised me _ but all I got was _.", "CAHe4", 2));
            localData.questions.Add(new mod_xyzzy_card("In the beginning there was _. And the Lord said, Let there be _.", "CAHe4", 2));
            localData.questions.Add(new mod_xyzzy_card("It lurks in the night. It hungers for flesh. This summer, no one is safe from _.", "CAHe4", 1));
            localData.questions.Add(new mod_xyzzy_card("Man, this is bullshit. Fuck _.", "CAHe4", 1));
            localData.questions.Add(new mod_xyzzy_card("She's up all night for good fun. I'm up all night for _.", "CAHe4", 1));
            localData.questions.Add(new mod_xyzzy_card("The Japanese have developed a smaller, more efficient version of _.", "CAHe4", 1));
            localData.questions.Add(new mod_xyzzy_card("This is the prime of my life. I'm young, hot, and full of _.", "CAHe4", 1));
            localData.questions.Add(new mod_xyzzy_card("This year's hottest album is _ by _.", "CAHe4", 2));
            localData.questions.Add(new mod_xyzzy_card("We never did find _, but along the way we sure learned a lot about _.", "CAHe4", 2));
            localData.questions.Add(new mod_xyzzy_card("Wes Anderson's new film tells the story of a precocious child coming to terms with _.", "CAHe4", 1));
            localData.questions.Add(new mod_xyzzy_card("What's fun until it gets weird?", "CAHe4", 1));
            localData.questions.Add(new mod_xyzzy_card("You've seen the bearded lady! You've seen the ring of fire! Now, ladies and gentlemen, feast your eyes upon _!", "CAHe4", 1));
            localData.questions.Add(new mod_xyzzy_card("_ may pass, but _ will last forever.", "CAHe4", 2));
            localData.questions.Add(new mod_xyzzy_card("_ will never be the same after _.", "CAHe4", 2));
            localData.questions.Add(new mod_xyzzy_card("You guys, I saw this crazy movie last night. It opens on _, and then there's some stuff about _, and then it ends with _.", "CAHe4", 3));
            localData.answers.Add(new mod_xyzzy_card("The biggest, blackest dick.", "Box"));
            localData.answers.Add(new mod_xyzzy_card("A box.", "Box"));
            localData.answers.Add(new mod_xyzzy_card("A box within a box.", "Box"));
            localData.answers.Add(new mod_xyzzy_card("A boxing match with a giant box.", "Box"));
            localData.answers.Add(new mod_xyzzy_card("A box of biscuits, a box of mixed biscuits, and a biscuit mixer.", "Box"));
            localData.answers.Add(new mod_xyzzy_card("An outbreak of smallbox.", "Box"));
            localData.answers.Add(new mod_xyzzy_card("The Boxcar Children.", "Box"));
            localData.answers.Add(new mod_xyzzy_card("A world without boxes.", "Box"));
            localData.answers.Add(new mod_xyzzy_card("Boxing up my feelings.", "Box"));
            localData.answers.Add(new mod_xyzzy_card("A box-shaped man.", "Box"));
            localData.answers.Add(new mod_xyzzy_card("A man-shaped box.", "Box"));
            localData.answers.Add(new mod_xyzzy_card("Something that looks like a box but turns out to be a crate.", "Box"));
            localData.answers.Add(new mod_xyzzy_card("A box that is conscious and wishes it weren't a box.", "Box"));
            localData.answers.Add(new mod_xyzzy_card("An alternate universe in which boxes store things inside of people.", "Box"));
            localData.answers.Add(new mod_xyzzy_card("The J15 Patriot Assault Box.", "Box"));
            localData.answers.Add(new mod_xyzzy_card("A box without hinges, key, or lid, yet golden treasure inside is hid.", "Box"));
            localData.answers.Add(new mod_xyzzy_card("Two midgets shitting into a box.", "Box"));
            localData.answers.Add(new mod_xyzzy_card("A falcon with a box on its head.", "Box"));
            localData.answers.Add(new mod_xyzzy_card("Being a motherfucking box.", "Box"));
            localData.answers.Add(new mod_xyzzy_card("Former President George W. Box.", "Box"));
            localData.answers.Add(new mod_xyzzy_card("Pandora's vagina.", "Box"));
            localData.answers.Add(new mod_xyzzy_card("Tom Baker, in nothing but a scarf.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Walking in on Jack Harkness doing your mom. And your dad.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("The buzzing noise that the Sonic Screwdriver makes.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Sharing a public restroom with a weeping angel.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Just now realizing that Torchwood is an anagram of Doctor Who.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Fifty years of fanfic.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Wanting to punch that teeny-bopper Whovian that's butthurt the new Doctor isn't in his twenties.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("The Doctor going back in time to solve a REAL problem: Twilight.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("A Doctor Who body pillow.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("The Silence.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("A Rusty Cyberman.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("The Doctor having a chance encounter with a couple of 80s metalheads.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Drunkenly drawing tally marks on your face.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("A shitty Doctor Who knock-knock joke.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Davros getting up on the wrong side of the bed.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("The Master, baiting the doctor into a trap.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("A Vashta Nerada that just wants a hug.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Wishing you could regenerate.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Kidnapping a barely-legal woman to time travel with.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Getting so much plastic surgery, you have to be framed and moisturized.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Quitting this panel after one round, because you are afraid of getting typecast.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("The poor costume decisions that were made in the 1970s.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("The Mary Jane Adventures.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Fondling a Dalek's slippery bits.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Sixteen feet of scarf bondage.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Air from my lungs.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Smoking 1000 cigarettes, just so you can sound like a Dalek when you talk.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Giving her the ol' plastic Mickey.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Companion Porn.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("An acid rain shower on Skaro.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Pointing to your crotch and saying Allons-y.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("A sonic screwdriver stuck on the vibrate setting.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Pouting in a rain storm and having to take a wicked piss.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("The poor decision that is having a staring contest with a weeping angel.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Sorry, this answer is only available in the fanfic version of Cards Against Con.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Plot holes so wide, you could drive a truck through them.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("A blinged-out TARDIS, blasting dubstep when it is travelling.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Rose Tyler's teeth.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("The Master singing Bad Case of Loving You.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Steven Moffat taking a big old dump in your Cheerios.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("K-9 humping your leg.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("A bigger, bluer TARDIS.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Robot Anne Robinson.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("A fez caked with semen.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("A GUITARDIS", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("The Celestial Toymaker's plaything.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Captain Jack Harkness.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("A furry writing BAD WOLF everywhere.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Your dyslexic friend that wants you to come watch a marathon of Doctor How.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Fapping to Billie Piper portraying a callgirl.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Being used as a plot device by Steven Moffat.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("A Costco-sized bag of Jelly Babies.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("A global simulcast that forces Whovians to see sunlight for the first time in ages.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("THE END OF TIME ITSELF!", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Finding Autons oddly attractive.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("The fuck machine dungeon of the Cybermen.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Glenn Beck convulsively puking as a brood of Daleks swarm in on him.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("River Song.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Low-budget special effects.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Eggs.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Dalek porn.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Taking a Doctor Poo.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("The big banana in your pocket.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Opening the door of the TARDIS and leaving a deuce in the time-space continuum.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("David Tennant.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Matt Smith.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Chistopher Eccleston.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Siltheen farts.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("A kid in a gas mask asking if you are his mummy.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("Fish fingering your custard.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("The hideousness that is Raxacoricofallapatorious.", "Gallifrey"));
            localData.answers.Add(new mod_xyzzy_card("An Ood getting a starring role in a hentai.", "Gallifrey"));
            localData.questions.Add(new mod_xyzzy_card("They found some more lost episodes! They were found in _.", "Gallifrey", 1));
            localData.questions.Add(new mod_xyzzy_card("The Doctor did it! He saved the world again! This time using a _.", "Gallifrey", 1));
            localData.questions.Add(new mod_xyzzy_card("_ was sent to save _.", "Gallifrey", 2));
            localData.questions.Add(new mod_xyzzy_card("I'd give up _ to travel with The Doctor.", "Gallifrey", 1));
            localData.questions.Add(new mod_xyzzy_card("The next Doctor Who spin-off is going to be called _.", "Gallifrey", 1));
            localData.questions.Add(new mod_xyzzy_card("Who should be the 13th doctor?", "Gallifrey", 1));
            localData.questions.Add(new mod_xyzzy_card("The Chameleon circuit is working again... somewhat. Instead of a phone booth, the TARDIS is now a _.", "Gallifrey", 1));
            localData.questions.Add(new mod_xyzzy_card("Originally, the 50th anniversary special was going to have _ appear, but the BBC decided against it in the end.", "Gallifrey", 1));
            localData.questions.Add(new mod_xyzzy_card("After we watch an episode, I've got some _-flavored Jelly Babies to hand out.", "Gallifrey", 1));
            localData.questions.Add(new mod_xyzzy_card("Wibbly-wobbly timey-wimey _.", "Gallifrey", 1));
            localData.questions.Add(new mod_xyzzy_card("What's going to be The Doctor's new catch phrase.", "Gallifrey", 1));
            localData.questions.Add(new mod_xyzzy_card("Bowties are _.", "Gallifrey", 1));
            localData.questions.Add(new mod_xyzzy_card("There's a new dance on Gallifrey, it's called the _.", "Gallifrey", 1));
            localData.questions.Add(new mod_xyzzy_card("They announced a LEGO Doctor Who game! Rumor has it that _ is an unlockable character.", "Gallifrey", 1));
            localData.questions.Add(new mod_xyzzy_card("FUN FACT: The Daleks were originally shaped to look like _.", "Gallifrey", 1));
            localData.questions.Add(new mod_xyzzy_card("At this new Doctor Who themed restaurant, you can get a free _ if you can eat a plate of bangers and mash in under 3 minutes.", "Gallifrey", 1));
            localData.questions.Add(new mod_xyzzy_card("According to the Daleks, _ is better at _.", "Gallifrey", 2));
            localData.questions.Add(new mod_xyzzy_card("Who is going to be The Doctor's next companion?", "Gallifrey", 1));
            localData.questions.Add(new mod_xyzzy_card("I think the BBC is losing it. They just released a Doctor Who-themed _.", "Gallifrey", 1));
            localData.questions.Add(new mod_xyzzy_card("It's a little-known fact that if you send a _ to the BBC, they will send you a picture of The Doctor.", "Gallifrey", 1));
            localData.questions.Add(new mod_xyzzy_card("I was okay with all the BAD WOLF graffiti, until someone wrote it on _.", "Gallifrey", 1));
            localData.questions.Add(new mod_xyzzy_card("Jack Harkness, I can't leave you alone for a minute! I turn around and you're trying to seduce _.", "Gallifrey", 1));
            localData.questions.Add(new mod_xyzzy_card("In all of time and space, you decide that _ is a good choice?!", "Gallifrey", 1));
            localData.questions.Add(new mod_xyzzy_card("Adipose were thought to be made of fat, but are really made of _.", "Gallifrey", 1));
            localData.questions.Add(new mod_xyzzy_card("I hear the next thing that will cause The Doctor to regenerate is _.", "Gallifrey", 1));
            localData.answers.Add(new mod_xyzzy_card("… . .-. . -. .. - -.—  (Serenity)", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("'Rails with pails.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Apple Juice.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("A bull penis cane.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("A chip in your heart that forces you to love.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("A dead Ms. Paint.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("A dominant Kankri.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("A five minute video of Cronus giving Kankri a blowjob.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("A mighty wwizard of wwhite science.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("A Nicolas Cage body pillow.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("A painting of a horse attacking a football player.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("A rapist cuttlefish.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("A slaughtered sperm whale.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("A smuppet in Dirk’s pants.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("A Strider sandwich.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("A VrisKan waffle.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Accidentally touching Gamze’s enormous codpiece.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Actual blind people who cosplay Terezi.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Alternian fine art.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Alternian rainbow-drinker romance novels.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("An acrobatic fucking pirouette.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Andrew Hussie.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Andrew Hussie’s lips.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Anonymous Soporifics Support.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Apple Juice.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Aradia Bot.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Aradia Megido.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Aradia’s charred, rotting corpse.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Aranea Serket.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Aranea's exposition stand.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Arguing over troll sexuality.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("ARquiusprite’s muscles.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Arthour the lusus.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("AVATAR.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Baby Dave.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Bard Quest.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Beating the shit out of Terezi.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Bec Noir.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Becoming Tumblr famous.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Being fuck deep in meowcats.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Being in a relationship with a non-Homestuck.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Being locked in a Prospitian prison.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Being the other guy.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("BETTY FUCKING CROCKER.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Binge reading every fanfiction for a pairing and then hating yourself a little bit.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("BL1ND JUST1C3.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Blackrom orgies.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Bro and Dave banging while Rose watches.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Bro.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Bro's rapping ventriloquism act.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Bro’s death.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("BUCKETS.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Butler Island.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("C4NDY R3D BLOOD >:]", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Caledscratch.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Caliborn.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Caliginous speed dating.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Calliope.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Can Town.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Can Town.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Cards Against Alternia.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Carlos Maraka.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Casey.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Centaur milk.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Charging down halls, shouting profanities and being silly.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Cherub m-preg.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Cherub mating rituals.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Chest of WHIMSY.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Cliched JohnKat fanfiction.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Cod Palace.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Cod Tier Gamzee.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Communism!", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Constantly breaking Hussie’s copyright.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Cosplay sex.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Cosplayers who do photo shoots in bondage (God bless them).", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Cosplayers who do photo shoots in bondage (God bless them).", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Cosplayers who don’t seal their paint.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Crabdad.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Creative uses for Aradia’s whip.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Cronus actually getting laid.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Cronus Ampora.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Dad Egbert/Dad Crocker.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Dad's pipe.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Dad’s fedora.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Damara Megido wearing white at her wedding.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Damara Megido.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Damara Megido’s existence.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Dante Basco.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Dating exclusively within the fandom.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Dave Strider.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Dave’s throbbing beef truncheon.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Dead parents.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Destroying clocks.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Developing a deep fear of the sound of clown horns after becoming a Homestuck.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Dirk Strider.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Dirk’s self-insert MLP fan character.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Discovering Sollux is red-blue colorblind.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Doc Scratch.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Drawing pornography for Caliborn.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Elf tears.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Equius cumming so hard he blows a hole straight through his partner.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Equius Zahhak.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Equius’s choice ass.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Equius’s copy of Fifty Shades of Neigh.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Equius’s towel.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Equius’s used towel pile.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Eridan Ampora.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Eridan crying after pailing Vriska for the first time.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Eridan stripping to make rent.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Eridan’s cape.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Eridan’s empty quadrants.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Eridan’s empty quadrants.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Eridan’s lowwer half.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Eridan’s upper half.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Falling into a pool of lava.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Fat Vriska.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Faygo.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Feferi Peixes.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Feferi’s voluptuous curves.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Fiduspawn.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Fifty fucking Nepetas.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Filling all of your quadrants.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Filling all of your quadrants.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Finding grey paint on your bathroom door three weeks after the last meetup.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Fis)( puns!", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Flighty broads.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Flipping the fuck out.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Game Bro Magazine.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Gamzee Makara.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Gamzee’s clown horns.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("gAmZeE’S pOtIoNs: 420 bOoNbUcKs.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Geromy.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Getting forked by a 2x3dent.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Gl'bgolyb. AKA Feferi’s fucking lusus.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Going to the bark side.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Grandpa Harley/Grandma English.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Grimbark Jade.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Groincobblers.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Gross misinterpretations of your favorite character.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Hateclown on the side.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Having STRONG surprise buttsex.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Hellmurder Island.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Hemostuck.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Hemostuck.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Her Imperious Condescenscion.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Her Imperious Condescension’s royal butt-plug collection.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Homesmut Voices.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Homestuck stealing all the fans from Hetalia and then subsequently watching all its fans leave for OFF and Danganronpa.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Homestuck.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Homosuck.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("HONK HONK, MOTHER FUCKER.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Horsearoni.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Horuss Zahhak.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Hot crossplayers.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Hunk Rump Magazine.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Hussie constantly breaking copyright and then telling his fans to not break his copyright.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Hussie constantly breaking copyright.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Hussie jacking it to our tears of anguish.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Jade Harley.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Jade’s dog penis and knot.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Jailbreak.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Jake English standing there like a fucking idiot.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Jake English.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Jake English’s assless chaps.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Jake English’s choice ass.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Jake English’s manhood.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Jane Crocker.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("John Egbert.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("John’s flaming homosexuality.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("John’s Prankster’s Gambit.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Just KNOWING that Slick is going to stab Ms. Paint.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Kanaya destroying Cantown.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Kanaya Maryam.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Kanaya's ashen promiscuity.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Kanaya’s chainsaw.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Kankri Vantas.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Karkat actually topping, for once.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Karkat and Jade’s adorable little of mpreg puppies.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Karkat dying of a burst blood vessel mid-rant.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Karkat going through puberty before every other troll and being, like, nine feet tall.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Karkat Tantrum Bingo.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Karkat Vantas.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Karkat’s ragegasm.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Karkat’s tiny, angry looking dick.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Kawaii Yaoi.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Kurloz Makara.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Lame bucket jokes.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Latula Pyrope.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Laying back and thinking of Alternia.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Leprechaun m-preg.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Liberty. Reason. Justice. Civility. Edification. Perfection. MAIL.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Lil' Cal's dead eyes.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Lil’ Cal.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Lil’ Cal’s raging boner.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Lil’ Hal.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Lil’ Seb.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Little children who poop hard in their baby ass diapers.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Lord English.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Lord English’s peg leg.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Lucky Charms.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Maid Equius.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Maple Hoof.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("March Eridan.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Masturbating while thinking of your OTP.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Masturbating while thinking of your OTP.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Maxing out your credit cards to buy Homestuck merchandise.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Meenah Piexes.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Meulin Leijon.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Mierfa Durgas.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Mierfa Durgas’ troll-horn nunchakus.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Mind honey.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Mister Dude, Sir Brah, Dood Dude, Vitamin D, Dude Esquire.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Mituna Captor.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Mom.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("MS Paint Adventures.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("MSPARP.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Murdering angels.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Muscle beasts.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("My Little Hoofbeast: Moirailigence Is Magic.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Nektan Whelan.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Neophyte Redglare.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Nepeta Leijon.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Nepeta violently mauling people with bad ships.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Nepeta’s heat cycle.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Nepeta’s shipping chart.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Never being able to look at apple juice, milk, buckets, or knitting needles without feeling a little bit uncormfortable.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Never dating a Serket.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Nic Cage saying boner.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("No homo.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Noping the fuck out of there.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Not shipping it.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Nyehs and wwehs.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Only cosplaying male characters when you get pregnant.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Overtaking entire conventions.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Paint splatters that look like troll cum.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("PantsKat.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Paradox slime.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Petstuck.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("PipeFan413.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Plush Rump Magazine.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Plush rump.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Porrim Maryam.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Porrim's condom stash.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Porrim’s motherly affections.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Post-apocalyptic shroudwear.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Problem Sleuth.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Recuperacoon.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Remembering that awkward time when Karkat called Future arachnidsGrip FAG.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Rose and Kanaya snuggling.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Rose Lalonde.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Rose telling John she’s a lesbian and they will never be together.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Rose’s review of My Immortal.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Roxy Lalonde.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Rufio.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Rufioh Nitram.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Sacred leggings.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("SBAHJ hentai doujinshi.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Schrödinger's Nepeta.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("SCIENCE WAND!", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Seadweller dick fins.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Selling your soul to Hussie.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Shippers.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Shipping it.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Shipping the fuck out of something.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Ships ending in -cest.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Shitty swords.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Shopping with Terezi.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Sick fires.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Skipping to Act 5.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Sleeping ten people to a room at conventions.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Sloppy inter-species makeouts.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Smuppets.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Sobbing uncontrollably while reading fanfiction.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Sollux Captor.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Sollux’s bifurcated bone bulge.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Sopor pies.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("SORD.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Soul portraits.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Species-swap fanfics.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Spidermom.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Staying up to three AM, cleaning the grey off every surface of your hotel room in a desperate bid to not get fined.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Stealing Tavros’s wheelchair.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Stridercest.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Sugoi Yuri.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("sweet bro and hell jeff.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("TAB.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Tavros Nitram.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Tavros’s wheelchair.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Telling Sollux what happens to male bees after sex.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Tentabulges.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Tentative thank-you stabs.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Terezi Pyrope.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("That dead crow with the sword through it.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("That human vacation with the giant red chimney-ass-hole.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("That shitty apple.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("That wonderful feeling when you take off your binder.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The special attachments we ALL know that Equius gave to AradiaBot.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The 7th Gate.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The animes.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The Condesce’s crotch.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The Condesce’s selfies.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The Dildo of Oglogoth.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The Disciple.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The Dolorosa.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The E%ecutor/Expatri8 Darkleer.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The Exiles.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The Felt.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The gays.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The glory that is BroJohn.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The Grand Highblood.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The Great Hiatus of 2013.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The green sun.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The guy who fingered an Ampora.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The Handmaid.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The hemospectrum.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The Hilarocaust.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The Homestuck drinking game (do a shot every time someone dies!)", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The Insane Clown Posse.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The little red arm-swingy-dealy thing or whatever it is called.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The Marquise Spinneret Mindfang.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The Mayor.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The mere concept of the Olive Garden.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The Midnight Crew.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The noises Mituna makes during sex.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The Orphaner Dualscar.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The Psiionic.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The ridiculous fact that some people communicate without luminous rear ends.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The Shipping Olympics.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The significant purposes, biologically speaking, of troll nipples.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The slammer.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The Sufferer/The Signless.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The Summoner.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The sweat-drenched, rippling muscles of several truly majestically endowed hoofbeasts.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The undeniable fact that Gamzee did nothing wrong.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The unimaginable amounts of cash Faygo’s been making off of Homestucks.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The Wrinklefucker.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("The Wrinklefucker.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Toilet displacement.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Topping from the bottom.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Triggers.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Troll blood.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Troll horns.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Troll Will Smith.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Trolls misunderstanding what Bucket List means.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Tumblr spoilers.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Tumblr user Egberts.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Tumblr user Pizza.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Tumblr.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Unreal air.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("UPD8!!!!!!!!", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("UPS delivery woman Nepeta.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Viceroy Bubbles von Salamancer.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Violent Blackrom sex.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Vodka Mutini.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Vodka.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Vriska dying after being stabbed by Terezi.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Vriska Serket.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Vriska’s SEXY sex tips for having SEXY SEX!", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Warhammer of Zillyhoo.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("What pumpkin?", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("When your favorite character dies.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("where MAKING THIS HAPEN", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Willingly filling buckets with Eridan.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Wondering if Meenah has a pitch crush on John, what with the attempted stabbings.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("World building!", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Your 300 pound matronly freight-train.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Your lusus giving you The Talk.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Your Mary Sue fantroll.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Your privilege.", "Alternia"));
            localData.answers.Add(new mod_xyzzy_card("Your significant other coming home and finding you in full grey cosplay.", "Alternia"));
            localData.questions.Add(new mod_xyzzy_card("_ makes the Homestuck fandom uncomfortable.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("_ stays awake at night, crying over _.", "Alternia", 2));
            localData.questions.Add(new mod_xyzzy_card("_ totally makes me question my sexuality.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("_. On the roof. Now.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("_. It keeps happening!", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Sacred leggings was a mistranslation. The Sufferer actually died in Sacred _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("After throwing _ at Karkat’s head, Dave made the intriguing discover that troll horns are very sensitive.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("AG: Who needs luck when you have _?", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("All _. All of it!", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Alternia’s political system was based upon _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Believe it or not, Kankri’s biggest trigger is _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Calliborn wants you to draw pornography of _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Dave Strider likes _, but only ironically.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Equius beats up Eridan for _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Everybody out of the god damn way. You’ve got a heart full of _, a soul full of _, and a body full of _. (Draw two, play three)", "Alternia", 3));
            localData.questions.Add(new mod_xyzzy_card("Feferi secretly hates _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("For Betty Crocker’s latest ad campaign/brainwashing scheme, she is using _ as inspiration.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("For his birthday, Dave gave John _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Fuckin’ _. How do they work?", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Gamzee not only likes using his clubs for juggling and strifing, he also uses them for_.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Getting a friend to read Homestuck is like _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("How do I live without _?", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Hussie died on his quest bed and rose as the fully realized _ of _.", "Alternia", 2));
            localData.questions.Add(new mod_xyzzy_card("Hussie unintentionally revealed that Homestuck will end with _ and _ consummating their relationship at last.", "Alternia", 2));
            localData.questions.Add(new mod_xyzzy_card("I am _. It’s me.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("I finally became Tumblr famous when I released a gifset of _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("I just found _ in my closet it is like fucking christmas up in here.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("I warned you about _, bro! I told you, dog!", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("In the final battle, John distracts Lord English by showing him _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("It’s hard, being _. It’s hard and no one understands.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("John is a good boy. And he loves _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("John may not be a homosexual, but he has a serious thing for _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Kanaya reached into her dead lusus’s stomach and retrieved _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Kanaya tells Karkat about _ to cheer him up.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Karkat gave our universe _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Latula and Porrin have decided to teach Kankri about the wonders of _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Little did they know, the key to defeating Lord English was actually _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Little known fact: Kurloz’s stitching is actually made out of _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Nanna baked a cake for John to commemorate _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Nepeta only likes Karkat for his _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Nepeta’s secret OTP is _ with _.", "Alternia", 2));
            localData.questions.Add(new mod_xyzzy_card("Nobody was surprised to find _ under Jade’s skirt. The surprise was she used it for/on _.", "Alternia", 2));
            localData.questions.Add(new mod_xyzzy_card("Porrim made Kankri a sweater to cover his _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Problem Sleuth had a hard time investigating _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Rose was rather disgusted when she started reading about _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Terezi can top anyone except _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("The hole in Kanaya’s stomach is so large, she can fit _ in it.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("The next thing Hussie will turn into a sex joke will be _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("The only way to beat Vriska in an eating contest is to put _ on the table.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("The real reason Terezi stabbed Vriska was to punish her for _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("The secret way to achieve God Tier is to die on top of _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("The thing that made Kankri break his vow of celibacy was _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Turns out, pre-entry prototyping with _ was not the best idea.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Vriska killed Spidermom with _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Vriska roleplays _ with Terezi as _.", "Alternia", 2));
            localData.questions.Add(new mod_xyzzy_card("Vriska’s greatest regret is _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Wear  _. Be _.", "Alternia", 2));
            localData.questions.Add(new mod_xyzzy_card("What did Jake get Dirk for his birthday?", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("What is the worst thing that Terezi ever licked?", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("What is your OT3? (Draw 2, play 3.)", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("What makes your kokoro go doki doki?", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("What's in the box, Jack?", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("When a bucket is unavailable, trolls with use _.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("When Dave received _ from his Bro for his 9th birthday, be felt a little warm inside.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Whenever I see _ on MSPARP, I disconnect immediately.", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("where doing it man. where MAKING _ HAPEN!", "Alternia", 1));
            localData.questions.Add(new mod_xyzzy_card("Your name is JOHN EGBERT and boy do you love _!", "Alternia", 1));
            localData.answers.Add(new mod_xyzzy_card("Making 77 cents on the dollar (unless you're Latina).", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Inspirational Dove chocolate wrappers.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("Hey, Susie. I know your job is _ but can you just grab me _? Thanks.", "Ladies Against Humanity", 2));
            localData.answers.Add(new mod_xyzzy_card("Masturbating to Ty Pennington.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Pretending you'll wear that bridesmaid dress again.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Mansplaining.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Beyonce thinkpieces.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Gabby Giffords' physical therapy.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Female genital mutilation.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("When the tampon's too low and you feel it with every step.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("A joke too funny for women to understand.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Meryl Streep selfies.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Hillary bitch-slapping Bill with a frozen tuna.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("The Bechdel Test.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Staph infections from dirty nail salons.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("This month in Cosmo: how to give your man _ at the expense of _.", "Ladies Against Humanity", 2));
            localData.answers.Add(new mod_xyzzy_card("Stalking wedding photos on Facebook, weeping softly.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Emma Goldman burning the whole motherfucker down.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Douches that smell like rain.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Rosa Parks' back seat.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Engagement photos on train tracks.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Choking on the ashes of Gloria Steinem's bras.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("The cold, hard truth that no lesbian has ever scissored.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Doing your kegels at work.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Misandry.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Abortion Barbie.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Telling a street harasser You know what? I *will* blow you.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Peggy Olson's cutthroat ambitions.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("A quickie with Rachel Maddow in the green room.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Forcefeeding Sheryl Sandberg the pages of Lean In, one by one.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Emma Watson, Emma Stone, EMMA THOMPSON BITCHES.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("Are you there, God? It's me, _", "Ladies Against Humanity", 1));
            localData.answers.Add(new mod_xyzzy_card("Dumpsters overflowing with whimisical save-the-date magnets.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("50 Shades of _.", "Ladies Against Humanity", 1));
            localData.answers.Add(new mod_xyzzy_card("The torture chamber where Kathryn Bigelow keeps James Cameron.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("A detailed vajazzling of Van Goh's Starry Night.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("It's not length, it's _.", "Ladies Against Humanity", 1));
            localData.answers.Add(new mod_xyzzy_card("STOP MAKING ME PRETEND TO CARE ABOUT YOUR WEDDING PINTEREST DARLA.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("The Chub Rub.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Chin hairs you pretend you don't have.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("Whatever, Peeta. You'll never understand my struggle with _.", "Ladies Against Humanity", 1));
            localData.answers.Add(new mod_xyzzy_card("A strongly worded letter to Netflix demanding the addition of The Good Wife.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Doubling up on sports bras.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("When a dog smells your crotch and you know exactly why.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("Men are from _, women are from _.", "Ladies Against Humanity", 2));
            localData.answers.Add(new mod_xyzzy_card("Malala's gunshot wounds.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Lactating when a stranger's baby cries on the train.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("Why does the Komen Foundation hate Planned Parenthood?", "Ladies Against Humanity", 1));
            localData.answers.Add(new mod_xyzzy_card("A new cookbook by Sylvia Plath.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Crying in the fitting room during bikini season.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("Math is hard. Let's go _!", "Ladies Against Humanity", 1));
            localData.answers.Add(new mod_xyzzy_card("Eating the entire bag.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("The latest proposal in the Texas legislature is to take away _ from women.", "Ladies Against Humanity", 1));
            localData.answers.Add(new mod_xyzzy_card("Scalding hot wax right there on your labia.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("The G-Spot, the Y-spot, the other spot you made up to confuse your partner.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("If you don't mind my asking, how *do* lesbians have sex?", "Ladies Against Humanity", 1));
            localData.answers.Add(new mod_xyzzy_card("The Golden Girls' never-ending supply of frozen cheesecake.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Forcefeeding Sheryl Sandberg the pages of Lean In, one by one.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Underboob swet like rancid milk.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("In her next romcom, Katherine Heigl plays a woman who falls in love with her boss's _.", "Ladies Against Humanity", 1));
            localData.answers.Add(new mod_xyzzy_card("Wondering whether your girl crush on Hermione constitutes pedophilia.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Taking a giant dump on the 18th green at the Augusta National Golf Club.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("A brown smudge equally likely to be period blood or chocolate.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("The Pantone color of the year is inspired by _.", "Ladies Against Humanity", 1));
            localData.answers.Add(new mod_xyzzy_card("A hand-crocheted Diva Cup case from Etsy.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("What is Olivia Pope's secret to removing red wine stains from white clothes?", "Ladies Against Humanity", 1));
            localData.answers.Add(new mod_xyzzy_card("Your gigantic crush on Jenna Lyons.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("A one-way ticket to Steubenville.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("A bodice-ripping 4-way with Alexander Skarsgard, Ian Somerhalder, and David Boreanaz.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("Why exactly was Alanis so mad at Uncle Joey?", "Ladies Against Humanity", 1));
            localData.answers.Add(new mod_xyzzy_card("Finger banging Michelle Rodriguez.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Princess Aurora maniacally devouring the still-beating heart of Maleficent.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("Why do men on the Internet send me pictures of _?", "Ladies Against Humanity", 1));
            localData.answers.Add(new mod_xyzzy_card("A tear stained copy of Reviving Ophelia.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("A misogynist dystopia set in a not-too-distant WAIT A MINUTE.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Dying your hair red like Angela Chase.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Daenerys Targaryen's fire-breathing vajayjay.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("What's my weapon of choice in the War on Women?", "Ladies Against Humanity", 1));
            localData.answers.Add(new mod_xyzzy_card("Resentfully clicking like on your boss's vacation photos.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Calmly informing your date that you understand the infield fly rule better than he does.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Tina Fey and Amy Poehler making out on a pile of Bitch magazines.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Only shaving up to the knee.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Meredith Grey's slut phase.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Shameful childhood memories of envying the wheelchair girl who got all the attention.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("What's Seth MacFarlane's problem?", "Ladies Against Humanity", 1));
            localData.answers.Add(new mod_xyzzy_card("Sort of wishing the baby on the plane would die.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Tenderly dominating Uncle Jesse from behind.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Stumbling on David Wright performing as Judy Garland in the East Village.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Urinating on yourself to prevent an assault.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("When a FOX News anchor causally references 'ebonics'.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Getting DPed by the Property Brothers on a custom granite countertop.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("I couldn't help but wonder: was it Mr. Big, or was it _?", "Ladies Against Humanity", 1));
            localData.answers.Add(new mod_xyzzy_card("Being compared to a Cathy Cartoon on Metafilter.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Kim Kardashian's placenta banh mi.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("What fell into my bra?", "Ladies Against Humanity", 1));
            localData.answers.Add(new mod_xyzzy_card("Asking Gilbert Gottfried to do the Iago voice during sex.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Watching Bethenny Frankel struggle for life in a churning sea of pre-mixed SkinnyGirl cocktails.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("An alternate version of the Washington Monument that looks kind of like a vagina.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("What's my preferred method of contraception?", "Ladies Against Humanity", 1));
            localData.answers.Add(new mod_xyzzy_card("Telling Pacey your innermost secrets in a canoe beneath the Capeside stars.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("The blue liquid from tampon commercials.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("Sofia Coppola's new film focuses on a wealthy young white woman feeling alienated by _.", "Ladies Against Humanity", 1));
            localData.answers.Add(new mod_xyzzy_card("Patti Stanger's line of jewelry.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("#solidarityisforwhitewomen.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("_: the Tori Amos song that changed my life", "Ladies Against Humanity", 1));
            localData.answers.Add(new mod_xyzzy_card("A candlelight vigil for Nicole Brown Smith.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("Something old, something new, something borrowed, and _.", "Ladies Against Humanity", 1));
            localData.answers.Add(new mod_xyzzy_card("Sexual fantasies involving Mindy Lahiri and a sumptuous coffeecake.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Tweeting Cory Booker about that guy walking behind you.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("A gender neutral, owl-themed baby announcement.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("Why can't we have nice things?", "Ladies Against Humanity", 1));
            localData.answers.Add(new mod_xyzzy_card("Being the only woman at the office-mandated sexual harassment training.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Asking Larry Summers increasingly difficult mathematical questions until Bar and Mat Mitzvahs are considered equally important.", "Ladies Against Humanity"));
            localData.answers.Add(new mod_xyzzy_card("Cramming Vladimir Putin full of Activia until he poops out Russia's homophobia.", "Ladies Against Humanity"));
            localData.questions.Add(new mod_xyzzy_card("In an attempt to reach a wider audience, the Royal Ontario Museum has opened an interactive exhibit on _.", "Canadian Conversion Kit", 1));
            localData.questions.Add(new mod_xyzzy_card("What's the Canadian government using to inspire rural students to suceed?", "Canadian Conversion Kit", 1));
            localData.questions.Add(new mod_xyzzy_card("in the next Bob and Doug McKenzie adventure, they have to find _ to uncover a sinister plot involving _ and _.", "Canadian Conversion Kit", 3));
            localData.questions.Add(new mod_xyzzy_card("Air canada guidelines now prohibit _ on airplanes.", "Canadian Conversion Kit", 1));
            localData.questions.Add(new mod_xyzzy_card("CTV presents _, the store of _.", "Canadian Conversion Kit", 2));
            localData.questions.Add(new mod_xyzzy_card("In Vancouver it is now legal to _.", "Canadian Conversion Kit", 1));
            localData.questions.Add(new mod_xyzzy_card("O Canada, we stand on guard for _.", "Canadian Conversion Kit", 1));
            localData.questions.Add(new mod_xyzzy_card("If _ came in two-fours, Canada would be more _.", "Canadian Conversion Kit", 2));
            localData.questions.Add(new mod_xyzzy_card("After unifying the GST and PST, the Government can now afford to provide _ for _.", "Canadian Conversion Kit", 2));
            localData.answers.Add(new mod_xyzzy_card("Snotsicles", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("Naked News.", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("Done Cherry's wardrobe.", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("Syrupy sex with a maple tree.", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("Terry Fox's prosthetic leg.", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("Canada: American's hat.", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("Homo milk.", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("Mr. Dressup.", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("The Front de Libération du Québec.", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("Heritage minutes.", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("The Royal Canadian Mounted Police", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("Stephen Harper", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("Burning down the White House.", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("Being Canadian", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("The Famous Five.", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("A Molson muscle.", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("An icy hand job from an Edmonton hooker.", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("Poutine", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("Schmirler the Curler.", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("The Official Languages Act. La Loi sure les langues officielles.", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("Newfies.", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("The CBC.", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("Graham Greene playing the same First Nations character on every TV show.", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("Killing a moose with your bare hands.", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("tim Hortons.", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("Quintland.", "Canadian Conversion Kit"));
            localData.answers.Add(new mod_xyzzy_card("Karla Momolka.", "Canadian Conversion Kit"));
            localData.questions.Add(new mod_xyzzy_card("When Verity snuck out for her nightly exhibitionistic jaunt, she didn't expect to come face to face with _.", "Nobilis Reed", 1));
            localData.questions.Add(new mod_xyzzy_card("Programmable clothes that can turn into any imaginable garment are great, but didn't the designers consider _?", "Nobilis Reed", 1));
            localData.questions.Add(new mod_xyzzy_card("Procurator Marcus Amandus set out to explore Lake Ontarius and discovered _.", "Nobilis Reed", 1));
            localData.questions.Add(new mod_xyzzy_card("You can satiate any sexual proclivity in Metamor City, if you look hard enough. Even _.", "Nobilis Reed", 1));
            localData.questions.Add(new mod_xyzzy_card("The new performers in the Artbodies strip club have raised a few eyebrows. Who'd have thought to combine _ with _?", "Nobilis Reed", 2));
            localData.questions.Add(new mod_xyzzy_card("In the next episode of Monster Whisperer, Dale Clearwater helps a _ whose tentacle monster is plagued with _.", "Nobilis Reed", 2));
            localData.questions.Add(new mod_xyzzy_card("The title of the new erotica anthology this month is: 'Like _.'", "Nobilis Reed", 1));
            localData.questions.Add(new mod_xyzzy_card("Because of the 'accident' yesterday, the Scout Academy now forbids cadets from having any contact whatsoever with _.", "Nobilis Reed", 1));
            localData.questions.Add(new mod_xyzzy_card("When confronted by an excited tentacle monster, it's best to just relax and think of _.", "Nobilis Reed", 1));
            localData.questions.Add(new mod_xyzzy_card("A Man, A Woman, and a _.", "Nobilis Reed", 1));
            localData.answers.Add(new mod_xyzzy_card("A depressed tentacle monster.", "Nobilis Reed"));
            localData.answers.Add(new mod_xyzzy_card("Pussy spiders.", "Nobilis Reed"));
            localData.answers.Add(new mod_xyzzy_card("The erotic possibilities of duct tape.", "Nobilis Reed"));
            localData.answers.Add(new mod_xyzzy_card("A starsip powered by orgasms.", "Nobilis Reed"));
            localData.answers.Add(new mod_xyzzy_card("A vagina dentata.", "Nobilis Reed"));
            localData.answers.Add(new mod_xyzzy_card("The periodic table of the awesoments.", "Nobilis Reed"));
            localData.answers.Add(new mod_xyzzy_card("An erotic audio drama, complete with moans and groans.", "Nobilis Reed"));
            localData.answers.Add(new mod_xyzzy_card("An addictive aerosol aphrodesiac.", "Nobilis Reed"));
            localData.answers.Add(new mod_xyzzy_card("Debugging nanobot code while hung over.", "Nobilis Reed"));
            localData.answers.Add(new mod_xyzzy_card("Never being able to touch your lover ever again.", "Nobilis Reed"));
            localData.answers.Add(new mod_xyzzy_card("Enormous breasts. I mean, seriously, 'how does she even walk' gigantic.", "Nobilis Reed"));
            localData.answers.Add(new mod_xyzzy_card("The sneaking suspicion that having sex with a theriomorph is actually bestiality.", "Nobilis Reed"));
            localData.answers.Add(new mod_xyzzy_card("Majoring in mad science.", "Nobilis Reed"));
            localData.answers.Add(new mod_xyzzy_card("Balticon!", "Nobilis Reed"));
            localData.answers.Add(new mod_xyzzy_card("A call on the listener feedback line that turns out to be a wrong number.", "Nobilis Reed"));
            localData.answers.Add(new mod_xyzzy_card("Penis enlargement that actually works.", "Nobilis Reed"));
            localData.answers.Add(new mod_xyzzy_card("Dirty Mad Libs.", "Nobilis Reed"));
            localData.answers.Add(new mod_xyzzy_card("Pregnant sex.", "Nobilis Reed"));
            localData.answers.Add(new mod_xyzzy_card("A killer corset", "Nobilis Reed"));
            localData.answers.Add(new mod_xyzzy_card("Professor Pinkertoot's Bosom Wax", "Nobilis Reed"));
            localData.answers.Add(new mod_xyzzy_card("Genderfuckery.", "Nobilis Reed"));
            localData.answers.Add(new mod_xyzzy_card("Zero gravity sex.", "Nobilis Reed"));
            localData.answers.Add(new mod_xyzzy_card("Badly translated Latin.", "Nobilis Reed"));
            localData.answers.Add(new mod_xyzzy_card("Detachable Boobs.", "Nobilis Reed"));
            localData.answers.Add(new mod_xyzzy_card("Making up for 10 years of shitty parenting with a PlayStation.", "christmas2013"));
            localData.answers.Add(new mod_xyzzy_card("Giving money and personal information to strangers on the Internet.", "christmas2013"));
            localData.answers.Add(new mod_xyzzy_card("A magical tablet containing a world of unlimited pornography.", "christmas2013"));
            localData.answers.Add(new mod_xyzzy_card("These low, low prices!", "christmas2013"));
            localData.answers.Add(new mod_xyzzy_card("Piece of shit Christmas cards with no money in them.", "christmas2013"));
            localData.answers.Add(new mod_xyzzy_card("Moses gargling Jesus's balls while Shiva and the Buddha penetrate his divine hand holes.", "christmas2013"));
            localData.answers.Add(new mod_xyzzy_card("The Hawaiian goddess Kapo and her flying detachable vagina.", "christmas2013"));
            localData.answers.Add(new mod_xyzzy_card("The shittier, Jewish version of Christmas.", "christmas2013"));
            localData.answers.Add(new mod_xyzzy_card("Swapping bodies with mom for a day.", "christmas2013"));
            localData.answers.Add(new mod_xyzzy_card("Finding out that Santa isn't real.", "christmas2013"));
            localData.answers.Add(new mod_xyzzy_card("Slicing a ham in icy silence.", "christmas2013"));
            localData.answers.Add(new mod_xyzzy_card("The Grinch's musty, cum-stained pelt.", "christmas2013"));
            localData.answers.Add(new mod_xyzzy_card("Rudolph's bright red balls.", "christmas2013"));
            localData.answers.Add(new mod_xyzzy_card("Jizzing into Santa's beard.", "christmas2013"));
            localData.answers.Add(new mod_xyzzy_card("Breeding elves for their priceless semen.", "christmas2013"));
            localData.answers.Add(new mod_xyzzy_card("The royal afterbirth.", "christmas2013"));
            localData.answers.Add(new mod_xyzzy_card("Congress's flaccid penises withering away beneath their suit pants.", "christmas2013"));
            localData.answers.Add(new mod_xyzzy_card("Having a strong opinion about Obamacare.", "christmas2013"));
            localData.answers.Add(new mod_xyzzy_card("A simultaneous nightmare and wet dream starring Sigourney Weaver.", "christmas2013"));
            localData.answers.Add(new mod_xyzzy_card("Being blind and deaf and having no limbs.", "christmas2013"));
            localData.answers.Add(new mod_xyzzy_card("People with cake in their mouths talking about how good cake is.", "christmas2013"));
            localData.questions.Add(new mod_xyzzy_card("But wait, there's more! If you order _ in the next 15 minutes, we'll throw in _ absolutely free!", "christmas2013", 2));
            localData.questions.Add(new mod_xyzzy_card("Because they are forbidden from masturbating, Mormons channel their repressed sexual energy into _.", "christmas2013", 1));
            localData.questions.Add(new mod_xyzzy_card("Blessed are you, Lord our God, creator of the universe, who has granted us _.", "christmas2013", 1));
            localData.questions.Add(new mod_xyzzy_card("I really hope my grandma doesn't ask me to explain _ again.", "christmas2013", 1));
            localData.questions.Add(new mod_xyzzy_card("What's the one thing that makes an elf instantly ejaculate?", "christmas2013", 1));
            localData.questions.Add(new mod_xyzzy_card("Here's what you can expect for the new year. Out:_. In: _.", "christmas2013", 2));
            localData.questions.Add(new mod_xyzzy_card("Revealed: Why He Really Resigned! Pope Benedict's Secret Struggle with _!", "christmas2013", 1));
            localData.questions.Add(new mod_xyzzy_card("Kids these days with their iPods and their Internet. In my day, all we needed to pass the time was _.", "christmas2013", 1));
            localData.questions.Add(new mod_xyzzy_card("GREETINGS HUMANS I AM _ BOT EXECUTING PROGRAM", "christmas2013", 1));
            localData.answers.Add(new mod_xyzzy_card("Sucking the President's dick.", "90s"));
            localData.answers.Add(new mod_xyzzy_card("Sunny D! Alright!", "90s"));
            localData.answers.Add(new mod_xyzzy_card("A mulatoo, an albino, a mosquito, and my libido.", "90s"));
            localData.answers.Add(new mod_xyzzy_card("Log.&trade;", "90s"));
            localData.answers.Add(new mod_xyzzy_card("Jerking off to a 10-second RealMedia clip.", "90s"));
            localData.answers.Add(new mod_xyzzy_card("Deregulating the mortgage market.", "90s"));
            localData.answers.Add(new mod_xyzzy_card("The Y2K bug.", "90s"));
            localData.answers.Add(new mod_xyzzy_card("Wearing Nicolas Cage's face.", "90s"));
            localData.answers.Add(new mod_xyzzy_card("Stabbing the shit out a Capri Sun.", "90s"));
            localData.answers.Add(new mod_xyzzy_card("Kurt Cobain's death.", "90s"));
            localData.answers.Add(new mod_xyzzy_card("Freeing Willy.", "90s"));
            localData.answers.Add(new mod_xyzzy_card("Liking big butts and not being able to lie about it.", "90s"));
            localData.answers.Add(new mod_xyzzy_card("The Great Cornholio.", "90s"));
            localData.answers.Add(new mod_xyzzy_card("Pure Moods, Vol. 1.", "90s"));
            localData.answers.Add(new mod_xyzzy_card("Yelling ”girl power!” and doing a high kick.", "90s"));
            localData.answers.Add(new mod_xyzzy_card("Pamela Anderson's boobs running in slow motion.", "90s"));
            localData.answers.Add(new mod_xyzzy_card("Pizza in the morning, pizza in the evening, pizza at supper time.", "90s"));
            localData.answers.Add(new mod_xyzzy_card("Angels interfering in an otherwise fair baseball game.", "90s"));
            localData.answers.Add(new mod_xyzzy_card("Getting caught up in the CROSSFIRE.&trade;", "90s"));
            localData.answers.Add(new mod_xyzzy_card("Patti Mayonnaise.", "90s"));
            localData.answers.Add(new mod_xyzzy_card("Cool 90s up-in-the-front hair.", "90s"));
            localData.answers.Add(new mod_xyzzy_card("Several Michael Keatons.", "90s"));
            localData.answers.Add(new mod_xyzzy_card("A bus that will explode if it goes under 50 miles per hour.", "90s"));
            localData.questions.Add(new mod_xyzzy_card("Siskel and Ebert have panned _ as ”poorly conceived” and ”sloppily executed.”", "90s", 1));
            localData.questions.Add(new mod_xyzzy_card("Up next on Nickelodeon: ”Clarissa Explains _.”", "90s", 1));
            localData.questions.Add(new mod_xyzzy_card("I'm a bitch, I'm a lover, I'm a child, I'm _.", "90s", 1));
            localData.questions.Add(new mod_xyzzy_card("How did Stella get her groove back?", "90s", 1));
            localData.questions.Add(new mod_xyzzy_card("Believe it or not, Jim Carrey can do a dead-on impression of _.", "90s", 1));
            localData.questions.Add(new mod_xyzzy_card("It's Morphin' Time! Mastodon! Pterodactyl! Triceratops! Sabertooth Tiger! _!", "90s", 1));
            localData.questions.Add(new mod_xyzzy_card("Tonight on SNICK: ”Are You Afraid of _?”", "90s", 1));
            localData.answers.Add(new mod_xyzzy_card("The black half of Barack Obama.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("The white half of Barack Obama.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Free ice cream, yo.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("A face full of horse cum.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Getting caught by the police and going to jail.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("My dead son's baseball glove.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Ejaculating live bees and the bees are angry.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Western standards of beauty.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Getting eaten alive by Guy Fieri.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Blowjobs for everyone.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Blackface.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Butt stuff.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Some shit-hot guitar licks.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Social justice warriors with flamethrowers of compassion.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Deez nuts.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("An unforgettable quinceañera.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("September 11th, 2001.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Daddy's credit card.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("A one-way ticket to Gary, Indiana.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("An uninterrupted history of imperialism and exploitation.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("P.F. Change himself.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Cutting off a flamingo's legs with garden shears.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("A giant powdery manbaby.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Anal fissures like you wouldn't believe.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Not believing in giraffes.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Getting drive-by shot.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("A team of lawyers.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("AIDS monkeys.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Wearing glasses and sounding smart.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Slowly easing down onto a cucumber.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("A whole new kind of porn.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("40 acres and a mule.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Boring vaginal sex.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Genghis Khan's DNA.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("The tiger that killed my father.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("My boyfriend's stupid penis.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Changing a person's mind with logic and facts.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Child support payments.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("The passage of time.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Going to a high school reunion on ketamine.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("A reason not to commit suicide.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Russian super-tuberculosis.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("A mouthful of potato salad.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("All these decorative pillows.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Figuring out how to have sex with a dolphin.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Being worshipped as the one true God.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("The basic suffering that pervades all of existence.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("The ghost of Marlon Brando.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Out-of-this-world bazongas.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Ancient Athenian boy-fucking", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("A crazy little thing called love.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("A zero-risk way to make $2,000 from home.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Seeing my village burned and my family slaughtered before my eyes.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Being paralyzed from the neck down.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Backwards knees.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Having been dead for a while.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("My first period.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Vegetarian options.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("The Abercrombie & Fitch lifestyle.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("The unbelievable world of mushrooms.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Being nine years old.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("The eight gay warlocks who dictate the rules of fashion.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("The swim team, all at once.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Denzel.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Unrelenting genital punishment.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Mom's new boyfriend.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("A disappointing salad.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("A powered exoskeleton.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Ennui.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Oil!", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Giant sperm from outer space.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Doing the right stuff to her nipples.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Too much cocaine.", "CAHe5"));
            localData.answers.Add(new mod_xyzzy_card("Seeing things from Hitler's perspective", "CAHe5"));
            localData.questions.Add(new mod_xyzzy_card("And today's soup is Cream of _.", "CAHe5", 1));
            localData.questions.Add(new mod_xyzzy_card("Now in bookstores: ”The Audacity of _,” by Barack Obama.", "CAHe5", 1));
            localData.questions.Add(new mod_xyzzy_card("WHOOO! God damn I love _!", "CAHe5", 1));
            localData.questions.Add(new mod_xyzzy_card("Do you lack energy? Does it sometimes feel like the whole world is _? Zoloft.&reg;", "CAHe5", 1));
            localData.questions.Add(new mod_xyzzy_card("Hi, this is Jim from accounting. We noticed a $1,200 charge labeled ”_.” Can you explain?", "CAHe5", 1));
            localData.questions.Add(new mod_xyzzy_card("Well if _ is good enough for _, it's good enough for me.", "CAHe5", 2));
            localData.questions.Add(new mod_xyzzy_card("Yo' mama so fat she _!", "CAHe5", 1));
            localData.questions.Add(new mod_xyzzy_card("What killed my boner?", "CAHe5", 1));
            localData.questions.Add(new mod_xyzzy_card("Don't forget! Beginning this week, Casual Friday will officially become ”_ Friday.”", "CAHe5", 1));
            localData.questions.Add(new mod_xyzzy_card("In his farewell address, George Washington famously warned Americans about the dangers of _.", "CAHe5", 1));
            localData.questions.Add(new mod_xyzzy_card("Having the worst day EVER. #_", "CAHe5", 1));
            localData.questions.Add(new mod_xyzzy_card("Get ready for the movie of the summer! One cop plays by the book. The other's only interested in one thing: _.", "CAHe5", 1));
            localData.questions.Add(new mod_xyzzy_card("What's making things awkward in the sauna?", "CAHe5", 1));
            localData.questions.Add(new mod_xyzzy_card("Life's pretty tough in the fast lane. That's why I never leave the house without _.", "CAHe5", 1));
            localData.questions.Add(new mod_xyzzy_card("Patient presents with _. Likely a result of _.", "CAHe5", 2));
            localData.questions.Add(new mod_xyzzy_card("Hi MTV! My name is Kendra, I live in Malibu, I'm into _, and I love to have a good time.", "CAHe5", 1));
            localData.questions.Add(new mod_xyzzy_card("Help me doctor, I've got _ in my butt!", "CAHe5", 1));
            localData.questions.Add(new mod_xyzzy_card("Why am I broke?", "CAHe5", 1));
            localData.questions.Add(new mod_xyzzy_card("I don't mean to brag, but they call me the Michael Jordan of _.", "CAHe5", 1));
            localData.questions.Add(new mod_xyzzy_card("Heed my voice, mortals! I am the god of _, and I will not tolerate _!", "CAHe5", 2));
            localData.questions.Add(new mod_xyzzy_card("Here at the Academy for Gifted Children, we allow students to explore _ at their own pace.", "CAHe5", 1));
            localData.questions.Add(new mod_xyzzy_card("Well what do you have to say for yourself, Casey? This is the third time you've been sent to the principal's office for _.", "CAHe5", 1));
            localData.questions.Add(new mod_xyzzy_card("In his new action comedy, Jackie Chan must fend off ninjas while also dealing with _.", "CAHe5", 1));
            localData.questions.Add(new mod_xyzzy_card("Armani suit: $1,000. Dinner for two at that swanky restaurant: $300. The look on her face when you surprise her with _: priceless.", "CAHe5", 1));
            localData.questions.Add(new mod_xyzzy_card("Do the Dew &reg; with our most extreme flavor yet! Get ready for Mountain Dew _!", "CAHe5", 1));
            localData.answers.Add(new mod_xyzzy_card("A bass drop so huge it tears the starry vault asunder to reveal the face of God.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Growing up chained to a radiator in perpetual darkness.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Shitting all over the floor like a bad, bad girl.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("A buttload of candy.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Sucking all the milk out of a yak.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Bullets.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("A man who is so cool that he rides on a motorcycle.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Sudden penis loss.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Getting all offended.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Crying and shitting and eating spaghetti.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("One unforgettable night of passion.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Being popular and good at sports.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Filling a man's anus with concrete.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Two whales fucking the shit out of eachother.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Cool, releatable cancer teens.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("The amount of gay I am.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("A possible Muslim.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Unsheathing my massive horse cock.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("A bowl of gourds.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("The male gaze.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("The power of the Dark Side.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Ripping a dog in half.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("A constant need for validation.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Meaningless sex.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Such a big boy.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Throwing stones at a man until he dies.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Cancer.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Like a million alligators.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Eating together like a god damn family for once.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Cute boys.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Pussy.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Being a terrible mother.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Never having sex again.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("A pizza guy who fucked up.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("A whole lotta woman.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("The all-new Nissan Pathfinder with 0.9% APR financing!", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("A peyote-fueled vision quest.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Kale.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Breastfeeding a ten year old.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Crippling social anxiety.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Immortality cream.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Texas.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Teaching a girl how to handjob the penis.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("A turd.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Shapes and colors.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Whatever you wish, mother.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("The haunting stare of an Iraqi child.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Robots who just want to party.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("A self-microwaving burrito.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Forgetting grandma's first name.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Our new Buffalo Chicken Dippers&reg;!", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Treasures beyond your wildest dreams.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Getting shot out of a cannon.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("The sweet song of sword against and the braying of mighty war beasts.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Walking into a glass door.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("The color puce.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Every ounce of charisma left in Mick Jagger's tired body.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("The eighth graders.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Setting my balls on fire and cartwheeling to Ohio.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("The dentist.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Gwyneth Paltrow's opinions.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Turning the rivers red with the blood of infidels.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Rabies.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Important news about Taylor Swift.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Ejaculating inside another man's wife.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Owls, the perfect predator.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Being John Malkovich.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Bathing in moonsblood and dancing around the ancient oak.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("An oppressed people with a vibrant culture.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("An overwhelming variety of cheeses.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Reading the entire End-User License Agreement.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Morpheus.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Peeing into a girl's butt to make a baby.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("Generally having no idea of what's going on.", "CAHe6"));
            localData.answers.Add(new mod_xyzzy_card("No longer finding any Cards Against Humanity card funny.", "CAHe6"));
            localData.questions.Add(new mod_xyzzy_card("I work my ass off all day for this family, and this is what I come home to? _!?", "CAHe6", 1));
            localData.questions.Add(new mod_xyzzy_card("I have a strict policy. First date, dinner. Second date, kiss. Third date, _.", "CAHe6", 1));
            localData.questions.Add(new mod_xyzzy_card("When I was a kid, we used to play Cowboys and _.", "CAHe6", 1));
            localData.questions.Add(new mod_xyzzy_card("This is America. If you don't work hard, you don't succeed. I don't care if you're black, white, purple, or _.", "CAHe6", 1));
            localData.questions.Add(new mod_xyzzy_card("You Won't Believe These 15 Hilarious _ Bloopers!", "CAHe6", 1));
            localData.questions.Add(new mod_xyzzy_card("James is a lonely boy. But when he discovers a secret door in his attic, he meets a magical new friend: _.", "CAHe6", 1));
            localData.questions.Add(new mod_xyzzy_card("Don't worry kid. It gets better. I've been living with _ for 20 years.", "CAHe6", 1));
            localData.questions.Add(new mod_xyzzy_card("My grandfather worked his way up from nothing. When he came to this country, all he had was the shoes on his feet and _.", "CAHe6", 1));
            localData.questions.Add(new mod_xyzzy_card("Behind every powerful man is _.", "CAHe6", 1));
            localData.questions.Add(new mod_xyzzy_card("You are not alone. Millions of Americans struggle with _ every day.", "CAHe6", 1));
            localData.questions.Add(new mod_xyzzy_card("Come to Dubai, where you can relax in our world famous spas, experience the nightlife, or simply enjoy _ by the poolside.", "CAHe6", 1));
            localData.questions.Add(new mod_xyzzy_card("This is madness. No! THIS IS _!", "CAHe6", 1));
            localData.questions.Add(new mod_xyzzy_card("Listen Gary, I like you. But if you want that corner office, you're going to have to show me _.", "CAHe6", 1));
            localData.questions.Add(new mod_xyzzy_card("I went to the desert and ate of the peyote cactus. Turns out my spirit animal is _.", "CAHe6", 1));
            localData.questions.Add(new mod_xyzzy_card("And would you like those buffalo wings mild, hot, or _?", "CAHe6", 1));
            localData.questions.Add(new mod_xyzzy_card("The six things I could never do without: oxygen, Facebook, chocolate, Netflix, friends, and _ LOL!", "CAHe6", 1));
            localData.questions.Add(new mod_xyzzy_card("Why won't you make love to me anymore? Is it _?", "CAHe6", 1));
            localData.questions.Add(new mod_xyzzy_card("Puberty is a time of change. You might notice hair growing in new places. You might develop an interest in _. This is normal.", "CAHe6", 1));
            localData.questions.Add(new mod_xyzzy_card("I'm sorry, Mrs. Chen, but there was nothing we could do. At 4:15 this morning, your son succumbed to _.", "CAHe6", 1));
            localData.questions.Add(new mod_xyzzy_card("I'm Miss Tennessee, and if I could make the world better by changing one thing, I would get rid of _.", "CAHe6", 1));
            localData.questions.Add(new mod_xyzzy_card("Tonight we will have sex. And afterwards, If you'd like, a little bit of _.", "CAHe6", 1));
            localData.questions.Add(new mod_xyzzy_card("Everybody join hands and close your eyes. Do you sense that? That's the presence of _ in this room.", "CAHe6", 1));
            localData.questions.Add(new mod_xyzzy_card("To become a true Yanomamo warrior, you must prove that you can withstand _ without crying out.", "CAHe6", 1));
            localData.questions.Add(new mod_xyzzy_card("Y'all ready to get this thing started? I'm Nick Cannon, and this is America's Got _.", "CAHe6", 1));
            localData.questions.Add(new mod_xyzzy_card("If you had to describe the Card Czar, using only one of the cards in your hand, which one would it be?", "CAHe6", 1));

            #endregion
        }


        
    }
}
