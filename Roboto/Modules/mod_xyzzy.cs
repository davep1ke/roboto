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
        public long playerID;
        public int wins = 0;
        public List<String> cardsInHand = new List<string>();
        public List<String> selectedCards = new List<string>();
        internal mod_xyzzy_player() { }
        public mod_xyzzy_player(string name, long playerID)
        {
            this.name = name;
            this.playerID = playerID;
        }

        internal void topUpCards(int nrCards, List<string> availableAnswers, long chatID)
        {

            
            
            while (cardsInHand.Count < nrCards)
            {
                //have we reached the end of the pack?
                if (availableAnswers.Count == 0)
                {
                    //get the chatData and top up the cards. 
                    mod_xyzzy_data chatData = (mod_xyzzy_data)Roboto.Settings.getChat(chatID).getPluginData(typeof(mod_xyzzy_data));
                    chatData.addAllAnswers();
                    TelegramAPI.SendMessage(chatID, "All answers have been used up, pack has been refilled!");
                }

                //pick a card
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

            List<string> invalidCards = new List<string>();
            foreach (string cardID in cardsInHand)
            {
                mod_xyzzy_card c = localData.getAnswerCard(cardID);
                if (c != null)
                {

                   answers.Add(c.text);
                }
                else
                {
                    Roboto.log.log("Answer card " + cardID + " not found! Removing from " + name + "'s hand", logging.loglevel.critical);
                    invalidCards.Add(cardID);
                }
            }
            //remove any invalid cards
            foreach(string cardID in invalidCards) { cardsInHand.Remove(cardID); }

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
                "xyzzy_filter - Shows the filters and their current status" + "\n\r" +
                "xyzzy_reset - Resets the scores";
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
                        "at any time - you will be included next time a question is asked. You will need to open a private chat to @" +
                        Roboto.Settings.botUserName + " if you haven't got one yet - unfortunately I am a stupid bot and can't do it myself :("
                        , false, -1, true);

                    //confirm number of questions
                    //TODO - wrap the TelegramAPI calls into methods in the plugin and pluginData classes. 
                    TelegramAPI.GetExpectedReply(c.chatID, m.userID, "How many questions do you want the round to last for (-1 for infinite)", true, typeof(mod_xyzzy), "SetGameLength");

                    //int nrQuestionID = TelegramAPI.GetReply(m.userID, "How many questions do you want the round to last for (-1 for infinite)", -1, true);
                    //localData.expectedReplies.Add(new mod_xyzzy_expectedReply(nrQuestionID, m.userID, c.chatID, "")); //this will last until the game is started. 
                    
                }
                //Start but there is an existing game
                else if (m.text_msg.StartsWith("/xyzzy_start"))
                {
                    chatData.getStatus();
                    processed = true;
                }


                //player joining
                else if (m.text_msg.StartsWith("/xyzzy_join") && chatData.status != mod_xyzzy_data.statusTypes.Stopped)
                {
                    //TODO - try send a test message. If it fails, tell the user to open a 1:1 chat.
                    long i = -1;
                    try
                    {
                        i = TelegramAPI.SendMessage(m.userID, "You joined the xyzzy game in " + m.chatName);
                        if (i == -1) { throw new Exception("Couldn't send join confirmation message"); }
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
                //Start but there is an existing game
                else if (m.text_msg.StartsWith("/xyzzy_join"))
                {
                    chatData.getStatus();
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
                    foreach (mod_xyzzy_player p in chatData.players) { players.Add(p.name); }
                    players.Add("Cancel");
                    string keyboard = TelegramAPI.createKeyboard(players, 2);
                    TelegramAPI.GetExpectedReply(m.chatID, m.userID, "Which player do you want to kick", true, typeof(mod_xyzzy), "kick", -1, true, keyboard);
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
                //Abandon, but no game in progress
                else if (m.text_msg.StartsWith("/xyzzy_abandon"))
                {
                    chatData.getStatus();
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
                    chatData.getStatus();
                    processed = true;
                }
                else if (m.text_msg.StartsWith("/xyzzy_filter"))
                {
                    string response = "The following pack filters are currently set. These can be changed when starting a new game : " + "\n\r" +
        chatData.getPackFilterStatus();
                    TelegramAPI.SendMessage(m.chatID, response, false, m.message_id);
                    processed = true;
                }
                
                else if (m.text_msg.StartsWith("/xyzzy_reset"))
                {
                    chatData.resetScores();
                    TelegramAPI.SendMessage(m.chatID, "Scores have been reset!", false, m.message_id);
                    processed = true;
                }
            }
            //has someone tried to do something unexpected in a private chat?
            else if (m.chatID == m.userID && m.text_msg.StartsWith("/xyzzy_"))
            {
                TelegramAPI.SendMessage(m.chatID, "To start a game, add me to a group chat, and type /xyzzy_start");
                processed = true;
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

            //todo sync packs where needed
        }

        public override string getStats()
        {
            int activePlayers = 0;
            int activeGames = 0;

            foreach (chat c in Roboto.Settings.chatData)
            {
                mod_xyzzy_data cd = c.getPluginData<mod_xyzzy_data>();
                if (cd.status != mod_xyzzy_data.statusTypes.Stopped)
                {
                    activeGames++;
                    activePlayers += cd.players.Count;
                }
                
            }
            
            string result = activePlayers.ToString() + " players in " + activeGames.ToString() + " active games";

            return result;

        }

        public override bool replyReceived(ExpectedReply e, message m, bool messageFailed = false)
        {
            bool processed = false;
            chat c = Roboto.Settings.getChat(e.chatID);
            mod_xyzzy_data chatData = c.getPluginData<mod_xyzzy_data>();

            //did one of our outbound messages fail?
            if (messageFailed)
            {
                if (e.messageData == "SetGameLength")
                {
                    TelegramAPI.SendMessage(e.chatID, "I need to be able to send you a direct message. Open up a chat with " + Roboto.Settings.botUserName + " and try again");
                    chatData.status = mod_xyzzy_data.statusTypes.Stopped;
                }
                processed = true;
            }


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
                //import a cardcast pack
                if (m.text_msg == "Import CardCast Pack")
                {
                    TelegramAPI.GetExpectedReply(chatData.chatID, m.userID, Helpers.cardCast.boilerPlate + "\n\r"
                        + "To import a pack, enter the pack code. To cancel, type 'Cancel'", true, typeof(mod_xyzzy), "cardCastImport");
                    chatData.status = mod_xyzzy_data.statusTypes.cardCastImport;
                }
                //enable/disable an existing pack
                else if (m.text_msg != "Continue")
                {
                    chatData.setPackFilter(m);
                    chatData.sendPackFilterMessage(m);
                }
                //no packs selected, retry
                else if (chatData.packFilter.Count == 0)
                {
                    chatData.sendPackFilterMessage(m);
                }
                //This is presumably a continue now...
                else
                {
                    chatData.addQuestions();
                    chatData.addAllAnswers();

                    //tell the player they can start when they want
                    string keyboard = TelegramAPI.createKeyboard(new List<string> { "start" }, 1);
                    TelegramAPI.GetExpectedReply(chatData.chatID, m.userID, "OK, to start the game once enough players have joined click the \"start\" button", true, typeof(mod_xyzzy), "Invites", -1, true, keyboard);
                    chatData.status = mod_xyzzy_data.statusTypes.Invites;
                }
                processed = true;
            }

            //Cardcast importing
            else if (chatData.status == mod_xyzzy_data.statusTypes.cardCastImport && e.messageData == "cardCastImport")
            {
                if (m.text_msg == "Cancel")
                {
                    //return to plugins
                    chatData.sendPackFilterMessage(m);
                    chatData.status = mod_xyzzy_data.statusTypes.setPackFilter;
                }
                else
                {
                    string importMessage;
                    Helpers.cardcast_pack pack = new Helpers.cardcast_pack();
                    bool success = importCardCastPack(m.text_msg, out pack, out importMessage);
                    if (success == true)
                    {
                        //reply to user
                        TelegramAPI.SendMessage(m.userID, importMessage);
                        //enable the filter
                        chatData.setPackFilter(m, pack.name);
                        //return to plugin selection
                        chatData.sendPackFilterMessage(m);
                        chatData.status = mod_xyzzy_data.statusTypes.setPackFilter;
                    }
                    else
                    {
                        TelegramAPI.GetExpectedReply(chatData.chatID, m.userID,
                        "Couldn't add the pack. " + importMessage + ". To import a pack, enter the pack code. To cancel, type 'Cancel'", true, typeof(mod_xyzzy), "cardCastImport");
                    }
                }
                processed = true;
            }

            //start the game proper
            else if (chatData.status == mod_xyzzy_data.statusTypes.Invites && e.messageData == "Invites" && m.text_msg == "start")
            {
                if (chatData.players.Count > 1)
                {
                    chatData.askQuestion();
                }
                else
                {
                    string keyboard = TelegramAPI.createKeyboard(new List<string> { "start" }, 1);
                    TelegramAPI.GetExpectedReply(chatData.chatID, m.userID, "Not enough players yet. To start the game once enough players have joined click the \"start\" button", true, typeof(mod_xyzzy), "Invites", -1, true, keyboard);
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
            else if (chatData.status == mod_xyzzy_data.statusTypes.Judging && e.messageData == "Judging" && m != null)
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
                    TelegramAPI.SendMessage(e.chatID, "Kicked " + p.name, false, -1, true);
                }
                chatData.check();

                processed = true;
            }

            return processed;
        }

        /// <summary>
        /// Import a cardcast pack into the xyzzy localdata
        /// </summary>
        /// <param name="packFilter"></param>
        /// <returns>String containing details of the pack and cards added. String will be empty if import failed.</returns>
        private bool importCardCastPack(string packCode, out Helpers.cardcast_pack pack, out string response)
        {

            bool success = localData.importCardCastPack(packCode, out pack, out response);
            
            return success;

        }

        public override void sampleData()
        {
            log("Adding stub sample packs");
            //Add packs for the standard CaH packs. These should be synced when we do startupChecks()
            localData.packs.Add(new Helpers.cardcast_pack("Cards Against Humanity", "CAHBS", "Cards Against Humanity"));
            localData.packs.Add(new Helpers.cardcast_pack("Expansion 1 - CAH", "CAHE1", "Expansion 1 - CAH"));
            localData.packs.Add(new Helpers.cardcast_pack("Expansion 2 - CAH", "CAHE2", "Expansion 2 - CAH"));
            localData.packs.Add(new Helpers.cardcast_pack("Expansion 3 - CAH", "CAHE3", "Expansion 3 - CAH"));
            localData.packs.Add(new Helpers.cardcast_pack("Expansion 4 - CAH", "CAHE4", "Expansion 4 - CAH"));
            localData.packs.Add(new Helpers.cardcast_pack("CAH Fifth Expansion", "EU6CJ", "CAH Fifth Expansion"));
            localData.packs.Add(new Helpers.cardcast_pack("CAH Sixth Expansion", "PEU3Q", "CAH Sixth Expansion"));

        }

        /// <summary>
        /// Startup checks and housekeeping
        /// </summary>
        public override void startupChecks()
        {
            //TODO - how does this differ from INIT ???
            log("Startup Checks");
            //DATAFIX: rename & replace any "good" packs from when they were manually loaded.
            foreach (mod_xyzzy_card q in localData.questions) { q.category = pack_replacements(q.category); }
            foreach (mod_xyzzy_card a in localData.answers) { a.category = pack_replacements(a.category); }

            //make sure our OOTB filters exist. Will be deduped afterwards. Messy as it relies on the new dummy pack being added AFTER the existing one, 
            //then keeping oldest pack first during dedupe.
            //TODO Can probably remove this when we have finished migrating everything
            sampleData();

            //make sure our local pack filter list is fully populated & dupe-free
            localData.startupChecks();

            //remove any duplicate cards
            //TODO - definately remove this. Can't dedupe properly as e.g. John Cena pack has multiple cards
            localData.removeDupeCards();

            //sync anything that needs it
            localData.packSyncCheck();


            //Replace any chat pack filters.
            foreach (chat c in Roboto.Settings.chatData)
            {
                mod_xyzzy_data chatData = (mod_xyzzy_data)c.getPluginData(typeof(mod_xyzzy_data));
                if (chatData != null)
                {
                    if (chatData.packFilter.Contains("Base") || chatData.packFilter.Contains(" Base")) { chatData.packFilter.Add("Cards Against Humanity"); }
                    if (chatData.packFilter.Contains("CAHe1") || chatData.packFilter.Contains(" CAHe1")) { chatData.packFilter.Add("Expansion 1 - CAH"); }
                    if (chatData.packFilter.Contains("CAHe2") || chatData.packFilter.Contains(" CAHe2")) { chatData.packFilter.Add("Expansion 2 - CAH"); }
                    if (chatData.packFilter.Contains("CAHe3") || chatData.packFilter.Contains(" CAHe3")) { chatData.packFilter.Add("Expansion 3 - CAH"); }
                    if (chatData.packFilter.Contains("CAHe4") || chatData.packFilter.Contains(" CAHe4")) { chatData.packFilter.Add("Expansion 4 - CAH"); }
                    if (chatData.packFilter.Contains("CAHe5") || chatData.packFilter.Contains(" CAHe5")) { chatData.packFilter.Add("CAH Fifth Expansion"); }
                    if (chatData.packFilter.Contains("CAHe6") || chatData.packFilter.Contains(" CAHe6")) { chatData.packFilter.Add("CAH Sixth Expansion"); }


                    chatData.packFilter.RemoveAll(x => x.Trim() == "Base");
                    chatData.packFilter.RemoveAll(x => x.Trim() == "CAHe1");
                    chatData.packFilter.RemoveAll(x => x.Trim() == "CAHe2");
                    chatData.packFilter.RemoveAll(x => x.Trim() == "CAHe3");
                    chatData.packFilter.RemoveAll(x => x.Trim() == "CAHe4");
                    chatData.packFilter.RemoveAll(x => x.Trim() == "CAHe5");
                    chatData.packFilter.RemoveAll(x => x.Trim() == "CAHe6");

                    //do a /check on all active chats
                    chatData.check();
                }
            }


        }

        private string pack_replacements(string input)
        {
            string result = input;
            switch (input.Trim())
            {
                case "Base":
                    result = "Cards Against Humanity";
                    break;
                case "CAHe1":
                    result = "Expansion 1 - CAH";
                    break;
                case "CAHe2":
                    result = "Expansion 2 - CAH";
                    break;
                case "CAHe3":
                    result = "Expansion 3 - CAH";
                    break;
                case "CAHe4":
                    result = "Expansion 4 - CAH";
                    break;
                case "CAHe5":
                    result = "CAH Fifth Expansion";
                    break;
                case "CAHe6":
                    result = "CAH Sixth Expansion";
                    break;

            }
            return result;
        }
    }
}
