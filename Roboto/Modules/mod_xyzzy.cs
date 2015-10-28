using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace Roboto.Modules
{

    /*
    Moved to settings / telegramAPI
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
    */

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
            
            if (m.text_msg == "bip")
            {
                int i = 1;
            }

            /* All of this kind of stuff should come in through replyRecieved now
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
            */


            if (c != null) //Setup needs to be done in a chat! Other replies will now have a chat object passed in here too!
            {
                //get current game data. 
                mod_xyzzy_data chatData = c.getPluginData<mod_xyzzy_data>();
                
                
                if (m.text_msg.StartsWith("/xyzzy_start") && chatData.status == mod_xyzzy_data.statusTypes.Stopped)
                {
                    //Start a new game!
                    chatData.reset();
                    Roboto.Settings.clearExpectedReplies(c.chatID, typeof(mod_xyzzy));
                    chatData.status = mod_xyzzy_data.statusTypes.SetGameLength;
                    //add the player that started the game
                    chatData.addPlayer(new mod_xyzzy_player(m.userFullName, m.userID));

                    //send out invites
                    TelegramAPI.SendMessage(m.chatID, m.userFullName + " is starting a new game of xyzzy! Type /xyzzy_join to join. You can join / leave " +
                        "at any time - you will be included next time a question is asked. You will need to open a private chat to " + 
                        Roboto.Settings.botUserName + " if you haven't got one yet - unfortunately I am a stupid bot and can't do it myself :(" 
                        , false, -1, true);

                    //confirm number of questions
                    //TODO - wrap the TelegramAPI calls into methods in the plugin and pluginData classes. 
                    TelegramAPI.GetExpectedReply(c.chatID, m.userID, "How many questions do you want the round to last for (-1 for infinite)", true, typeof(mod_xyzzy), "SetGameLength");

                    //int nrQuestionID = TelegramAPI.GetReply(m.userID, "How many questions do you want the round to last for (-1 for infinite)", -1, true);
                    //localData.expectedReplies.Add(new mod_xyzzy_expectedReply(nrQuestionID, m.userID, c.chatID, "")); //this will last until the game is started. 

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
                    TelegramAPI.GetExpectedReply(m.chatID, m.userID,  "Which player do you want to kick", true, typeof(mod_xyzzy), "kick", -1, true, keyboard);
                    processed = true;
                }
                //abandon game
                else if (m.text_msg.StartsWith("/xyzzy_abandon") && chatData.status != mod_xyzzy_data.statusTypes.Stopped)
                {
                    chatData.status = mod_xyzzy_data.statusTypes.Stopped;
                    Roboto.Settings.clearExpectedReplies(c.chatID, typeof(mod_xyzzy));
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
                        //order the list of players
                        List<mod_xyzzy_player> orderedPlayers = chatData.players.OrderByDescending(e => e.wins).ToList();

                        foreach (mod_xyzzy_player p in orderedPlayers)
                        {
                            response += p.name + " - " + p.wins.ToString() + " points. \n\r";
                        }
                        
                        switch (chatData.status)
                        {
                            case mod_xyzzy_data.statusTypes.Question:
                                response += "The current question is : " + "\n\r" +
                                    localData.getQuestionCard(chatData.currentQuestion).text + "\n\r" +
                                    "The following responses are outstanding :";
                                foreach (ExpectedReply r in Roboto.Settings.getExpectedReplies(typeof(mod_xyzzy), c.chatID, -1, "Question"))
                                {
                                    if (r.chatID == c.chatID)
                                    {
                                        mod_xyzzy_player p = chatData.getPlayer(r.userID);
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
            //TODO - time people out and stuff.
            throw new NotImplementedException();
        }

        public override bool replyReceived(ExpectedReply e, message m)
        {
            bool processed = false;
            chat c = Roboto.Settings.getChat(e.chatID);
            mod_xyzzy_data chatData = c.getPluginData<mod_xyzzy_data>();

            //Set up the game, once we get a reply from the user. 
            if (chatData.status == mod_xyzzy_data.statusTypes.SetGameLength && e.messageData == "SetGameLength")
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
                    TelegramAPI.GetExpectedReply(m.chatID, m.userID, m.text_msg + " is not a valid number. How many questions do you want the round to last for? -1 for infinite", true, typeof(mod_xyzzy), "SetGameLength");
                }
                processed = true;
            }

            //Set up the game filter, once we get a reply from the user. 
            else if (chatData.status == mod_xyzzy_data.statusTypes.setPackFilter && e.messageData == "setPackFilter")
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
                    string keyboard = TelegramAPI.createKeyboard(new List<string> { "start" }, 1);
                    int expectedMessageID = TelegramAPI.GetExpectedReply(chatData.chatID, m.userID, "OK, to start the game once enough players have joined click the \"start\" button", true,typeof(mod_xyzzy), "Invites", -1, true, keyboard);
                    chatData.status = mod_xyzzy_data.statusTypes.Invites;
                }
                processed = true;
            }


            //start the game proper
            else if (chatData.status == mod_xyzzy_data.statusTypes.Invites && e.messageData == "Invites" && m.text_msg == "start")
            {
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
            else if (chatData.status == mod_xyzzy_data.statusTypes.Question && e.messageData == "Question")
            {
                bool answerAccepted = chatData.logAnswer(m.userID, m.text_msg);
                processed = true;
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
            else if (chatData.status == mod_xyzzy_data.statusTypes.Judging && e.messageData == "Judging")
            {
                bool success = chatData.judgesResponse(m.text_msg);
               
                processed = true;
            }

            else if (e.messageData == "kick")
            {
                mod_xyzzy_player p = chatData.getPlayer(m.text_msg);
                if (p != null)
                {
                    chatData.players.Remove(p);
                    TelegramAPI.SendMessage(e.chatID, "Kicked " + p.name, false,-1 , true);

                }
                chatData.check();
                
                processed = true;
            }

            return processed;
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
