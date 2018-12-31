using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace RobotoChatBot.Modules
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

        public string name_markdownsafe
        {
            get
            {
                return Helpers.common.removeMarkDownChars(name);
            }
        }


        public bool fuckedWith = false;
        public string handle = "";
        public long playerID;
        public int wins = 0;
        public List<String> cardsInHand = new List<string>();
        public List<String> selectedCards = new List<string>();
        internal mod_xyzzy_player() { }
        public mod_xyzzy_player(string name, string handle, long playerID)
        {
            this.name = name;
            this.handle = handle;
            this.playerID = playerID;
        }

        public override string ToString()
        {

            string response = " " + name;
            if (handle != "") { response += " (@" + handle + ")"; }

            return response;
        }

        public string ToString(bool markdownSafe)
        {
            if (markdownSafe)
            {
                string response = " " + name_markdownsafe ;
                String handle_safe =  Helpers.common.removeMarkDownChars(handle);
                if (handle != "" && handle == handle_safe)
                {
                    response += " (@" + handle_safe + ")";
                }
                else if (handle != handle_safe)
                {
                    Roboto.log.log("Skipping handle for " + handle + " as contains markdown", logging.loglevel.low);
                }
                return response;
            }
            else
            {
                return ToString();
            }
        }

        internal void topUpCards(int nrCards, List<string> availableAnswers, long chatID)
        {
            
            while (cardsInHand.Count < nrCards)
            {
                //have we reached the end of the pack?
                if (availableAnswers.Count == 0)
                {
                    //get the chatData and top up the cards. 
                    mod_xyzzy_chatdata chatData = (mod_xyzzy_chatdata)Roboto.Settings.getChat(chatID).getPluginData(typeof(mod_xyzzy_chatdata));
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

        public void toggleFuckWith()
        {

            if (fuckedWith == true) { fuckedWith = false; }
            else { fuckedWith = true; }
        }

        public string getPointsMessage()
        {
            
            string response = "\n\r" + name_markdownsafe + " - ";
            if (!fuckedWith) { return response + wins + " points."; }
            else
            {
                string[] suffixes = { "INT", "XP", "Points", "Sq. Ft.", "ft, 6 inches", "mm", "out of 10. Must try harder.", "Buzzards", "Buzzards/m/s²", "m/s²" };

                //want a multipler between -1 and 0.5.
                float multiplier = (50 - settings.getRandom(150))/100f ;
                int randomSuffix = settings.getRandom(suffixes.Count() - 1);
                int newscore = Convert.ToInt32(wins * multiplier);
                response += newscore.ToString() + " " + suffixes[randomSuffix];
                
            }
            return response;
        }

        public bool setScore(int playerScore)
        {
            Roboto.log.log("Overwrote " + this.ToString() + "'s points with " + playerScore, logging.loglevel.warn);
            wins = playerScore;
            return true;
        }
    }


    /// <summary>
    /// Represents a xyzzy card
    /// </summary>
    public class mod_xyzzy_card
    {
        public string uniqueID = Guid.NewGuid().ToString();
        public String text;
        [System.Obsolete("use Pack (Guid)")]
        public String category; //what pack did the card come from

        //shitty workaround to allow us to load in the cateogry info temporarily. - http://stackoverflow.com/questions/5096926/what-is-the-get-set-syntax-in-c
        [XmlElement("category")]
        public string TempCategory
        {
            #pragma warning disable 612, 618
            get { return category; }
            set { category = value; }
            #pragma warning restore 612, 618
        }

        public Guid packID;
        public int nrAnswers = -1; 

        internal mod_xyzzy_card() { }
        public mod_xyzzy_card(String text, Guid packID, int nrAnswers = -1)
        {
            this.text = text;
            this.packID = packID;
            this.nrAnswers = nrAnswers;
        }

        public override string ToString()
        {
            return text;
        }

    }


    /// <summary>
    /// The XXZZY Plugin
    /// </summary>
    public class mod_xyzzy : RobotoModuleTemplate
    {
        public static Guid primaryPackID = new Guid("FACEBABE-DEAD-BEEF-ABBA-FACEBABEFADE");
        public static Guid AllPacksEnabledID = Guid.Empty;

        private mod_xyzzy_coredata localData;
        

        public override void init()
        {
            pluginDataType = typeof(mod_xyzzy_coredata);
            pluginChatDataType = typeof(mod_xyzzy_chatdata);

            chatHook = true;
            chatEvenIfAlreadyMatched = false;
            chatPriority = 3;

            backgroundHook = true;
            backgroundMins = 1; //every 1 min, check the latest 20 chats
            
        }

        public override string getMethodDescriptions()
        {
            return
                "xyzzy_start - Starts a new game of xyzzy with the players in the chat" + "\n\r" +
                "xyzzy_settings - Change the various game settings" + "\n\r" +
                "xyzzy_get_settings - Get the current game settings" + "\n\r" +
                "xyzzy_join - Join a game of xyzzy that is in progress, or about to start" + "\n\r" +
                "xyzzy_leave - Leave a game of xyzzy" + "\n\r" +
                //"xyzzy_extend - Extends a running game with more cards, or restarts a game that has just stopped" + "\n\r" +
                "xyzzy_status - Gets the current status of the game";
                //"xyzzy_filter - Shows the filters and their current status" + "\n\r" +
        }

        public override string getWelcomeDescriptions()
        {
            return "To start a new game of Chat Against Humanity, type /xyzzy_start in a group chat window. You'll need a couple of friends, and you will all need to open a private message session with the bot to play." + "\n\r" + 
                "Chat Against Humanity is a virtual card game you can play with friends over Telegram! Each round, the dealer will ask a question, players will get a private message where they should pick their best answer card from a list of cards in their hand, and the dealer can judge the best answer from the replies.";

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

            Roboto.Settings.stats.registerStatType("New Games Started", this.GetType(), System.Drawing.Color.Aqua);
            Roboto.Settings.stats.registerStatType("Games Ended", this.GetType(), System.Drawing.Color.Orange);
            Roboto.Settings.stats.registerStatType("Hands Played", this.GetType(), System.Drawing.Color.Olive);
            Roboto.Settings.stats.registerStatType("Packs Synced", this.GetType(), System.Drawing.Color.DarkBlue );
            Roboto.Settings.stats.registerStatType("Bad Responses", this.GetType(), System.Drawing.Color.Olive);
            Roboto.Settings.stats.registerStatType("Active Games", this.GetType(), System.Drawing.Color.Green, stats.displaymode.line, stats.statmode.absolute);
            Roboto.Settings.stats.registerStatType("Active Players", this.GetType(), System.Drawing.Color.Blue, stats.displaymode.line, stats.statmode.absolute);
            Roboto.Settings.stats.registerStatType("Background Wait", this.GetType(), System.Drawing.Color.Red, stats.displaymode.line, stats.statmode.absolute);
            Roboto.Settings.stats.registerStatType("Background Wait (Quickcheck)", this.GetType(), System.Drawing.Color.Red, stats.displaymode.line, stats.statmode.absolute);
            Roboto.Settings.stats.registerStatType("Background Wait (Pack Sync)", this.GetType(), System.Drawing.Color.Cyan, stats.displaymode.line, stats.statmode.absolute);

            Console.WriteLine(localData.questions.Count.ToString() + " questions and " + localData.answers.Count.ToString() + " answers loaded for xyzzy");

        }

        public override void initChatData(chat c)
        {
            mod_xyzzy_chatdata chatData = c.getPluginData<mod_xyzzy_chatdata>();
           
            if (chatData == null)
            {
                //Data doesnt exist, create, populate with sample data and register for saving
                chatData = new mod_xyzzy_chatdata();
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
                mod_xyzzy_chatdata chatData = c.getPluginData<mod_xyzzy_chatdata>();

                if (chatData == null)
                {
                    log("Chatdata doesnt exist! creating.", logging.loglevel.high);
                    initChatData(c);
                    chatData = c.getPluginData<mod_xyzzy_chatdata>();

                    if (chatData == null)
                    {
                        log("Chatdata still doesnt exist! creating.", logging.loglevel.critical);
                    }
                }

                if (m.text_msg.StartsWith("/xyzzy_start") && chatData.status == xyzzy_Statuses.Stopped)
                {

                    //Tidy up 
                    chatData.reset();

                    Roboto.Settings.stats.logStat(new statItem("New Games Started", this.GetType()));
                    //Start a new game!
                    
                    //try and send the opening message


                    //use defaults or configure game
                    string kb = TelegramAPI.createKeyboard(new List<string>() { "Use Defaults", "Configure Game", "Cancel" }, 2);
                    
                    long messageID = TelegramAPI.GetExpectedReply(c.chatID, m.userID, "Do you want to start the game with the default settings, or set advanced optons first? You can change these options later with /xyzzy_settings", true, typeof(mod_xyzzy), "useDefaults", m.userFullName, -1,false,kb);

                    if (messageID == long.MinValue)
                    {
                        //no private message session
                        TelegramAPI.SendMessage(m.chatID, m.userFullName + " needs to open a private chat to @" +
                            Roboto.Settings.botUserName + " to be able to start a game", m.userFullName, false, -1, true);
                    }
                    else
                    {
                        //message went out successfully, start setting it up proper

                        chatData.setStatus(xyzzy_Statuses.useDefaults);
                        //add the player that started the game
                        chatData.addPlayer(new mod_xyzzy_player(m.userFullName, m.userHandle, m.userID));

                        //send out invites
                        TelegramAPI.SendMessage(m.chatID, m.userFullName + " is starting a new game of xyzzy! Type /xyzzy_join to join. You can join / leave " +
                            "at any time - you will be included next time a question is asked. You will need to open a private chat to @" +
                            Roboto.Settings.botUserName + " if you haven't got one yet - unfortunately I am a stupid bot and can't do it myself :("
                            , m.userFullName, false, -1, true);
                    }
                    
                    //TODO - wrap the TelegramAPI calls into methods in the plugin and pluginData classes.                    
                    
                }
                //Start but there is an existing game
                else if (m.text_msg.StartsWith("/xyzzy_start"))
                {
                    chatData.getStatus();
                    processed = true;
                }


                //player joining
                else if (m.text_msg.StartsWith("/xyzzy_join") && chatData.status != xyzzy_Statuses.Stopped)
                {
                    //try send a test message. If it fails, tell the user to open a 1:1 chat.
                    long i = -1;
                    try
                    {
                        i = TelegramAPI.SendMessage(m.userID, "You joined the xyzzy game in " + m.chatName);
                        if (i == -1)
                        {
                            log("Adding user, but the outbound message is still queued", logging.loglevel.verbose);
                            TelegramAPI.SendMessage(m.chatID, "Sent " + m.userFullName + " a message, but I'm waiting for them to reply to another question. "
                                + m.userFullName + " is in the game, but will need to clear their other PMs before they see any questions. ", m.userFullName, false, m.message_id);

                        }
                        else if (i < 0)
                        {
                            log("Adding user, but message blocked, abandoning", logging.loglevel.warn);
                            TelegramAPI.SendMessage(m.chatID, "Couldn't add " + m.userFullName + " to the game, as I couldnt send them a message. "
                               + m.userFullName + " probably needs to open a chat session with me. "
                               + "Create a message session, then try /xyzzy_join again.", m.userFullName, false, m.message_id);
                        }

                    
                        if (i != long.MinValue) //if we didnt get an error sending the message
                        {
                            log("Adding user processing", logging.loglevel.verbose);
                            bool added = chatData.addPlayer(new mod_xyzzy_player(m.userFullName, m.userHandle, m.userID));
                            if (added) { TelegramAPI.SendMessage(c.chatID, m.userFullName + " has joined the game"); }
                            else { TelegramAPI.SendMessage(c.chatID, m.userFullName + " is already in the game"); }
                        }
                    }
                    catch
                    {
                        //shouldnt actually get here. "Normal" errors should result in a -1 (queued) or a minvalue (forbidden)
                        log("Other excpetion sending add player confirmation message", logging.loglevel.high);
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
                    //if (removed) { TelegramAPI.SendMessage(c.chatID, m.userFullName + " has left the game"); }
                    //else { TelegramAPI.SendMessage(c.chatID, m.userFullName + " isnt part of the game, and can't be removed!"); }
                    processed = true;
                }
               
                else if (m.text_msg.StartsWith("/xyzzy_status"))
                {
                    chatData.getStatus();
                    processed = true;
                }
                else if (m.text_msg.StartsWith("/xyzzy_settings"))
                {
                    chatData.sendSettingsMessage(m);
                    processed = true;
                }
                else if (m.text_msg.StartsWith("/xyzzy_get_settings"))
                {
                    chatData.sendSettingsMsgToChat();
                    processed = true;
                }


                /*Moved to the /xyzzy_settings command
                else if (m.text_msg.StartsWith("/xyzzy_setFilter"))
                {
                    chatData.sendSettingsMessage(m);
                    processed = true;
                }

                
                else if (m.text_msg.StartsWith("/xyzzy_filter"))
                {
                    string response = "The following pack filters are currently set. These can be changed when starting a new game : " + "\n\r" +
        chatData.getPackFilterStatus();
                    TelegramAPI.SendMessage(m.chatID, response, false, m.message_id);
                    processed = true;
                }
                //set the filter (inflight)
                else if (m.text_msg.StartsWith("/xyzzy_setFilter"))
                {
                    chatData.sendPackFilterMessage(m, 1);
                    
                    processed = true;
                }
                //set the filter (inflight)
                else if (m.text_msg.StartsWith("/xyzzy_reDeal"))
                {
                    TelegramAPI.SendMessage(m.chatID, "Resetting everyone's cards, and shuffled the decks", false, m.message_id);
                    chatData.reDeal();
                    
                    processed = true;
                }

                else if (m.text_msg.StartsWith("/xyzzy_reset"))
                {
                    chatData.resetScores();
                    TelegramAPI.SendMessage(m.chatID, "Scores have been reset!", false, m.message_id);
                    processed = true;
                }

                //inflite options
                else if (m.text_msg.StartsWith("/xyzzy_setTimeout"))
                {
                    chatData.askMaxTimeout(m.userID);
                }
                else if (m.text_msg.StartsWith("/xyzzy_setThrottle"))
                {
                    chatData.askMinTimeout(m.userID);
                }
                */
            }
            //has someone tried to do something unexpected in a private chat?
            else if (m.chatID == m.userID)
            {
                //player leaving from the private chat
                if (m.text_msg.StartsWith("/xyzzy_leave"))
                {
                    //get list of active games that they are in
                    List<string> activeGames = new List<string>();
                    
                    //search active games and add ids to a list
                    foreach (chat ca in Roboto.Settings.chatData)
                    {
                        mod_xyzzy_chatdata plug = (mod_xyzzy_chatdata)ca.getPluginData(typeof(mod_xyzzy_chatdata), true);
                        if (plug != null && plug.players.Where(x => x.playerID == m.userID).Count() > 0) { activeGames.Add(ca.chatTitle + " (" + ca.chatID + ")"  ); }
                    }

                    if (activeGames.Count() > 0)
                    {
                        //make a keyboard and send leave message to user.
                        activeGames.Add("Cancel");
                        string kb = TelegramAPI.createKeyboard(activeGames, 1);
                        long messageID = TelegramAPI.GetExpectedReply(0, m.userID, "Which game would you like to leave?", true, typeof(mod_xyzzy), "leaveGamePickGroup", m.userFullName, -1, false, kb, false, false, true);
                    }
                    else
                    {
                        TelegramAPI.SendMessage(m.userID, "You are not in any active games.", null, false, m.message_id, true, true);
                    }


                    processed = true;
                }


                else if (m.text_msg.StartsWith("/xyzzy_"))
                {
                    TelegramAPI.SendMessage(m.chatID, "To start a game, add me to a group chat, and type /xyzzy_start");
                    processed = true;
                }
            }


            return processed;
        }



        protected override void backgroundProcessing()
        {
            mod_xyzzy_coredata localdata = (mod_xyzzy_coredata)getPluginData();
            logging.longOp lo_bg = new logging.longOp("XYZZY - background", 5);

            //update stats
            int activeGames = 0;
            int activePlayers = 0;
            foreach (chat c in Roboto.Settings.chatData)
            {
                mod_xyzzy_chatdata chatData = c.getPluginData<mod_xyzzy_chatdata>();
                if (chatData != null && chatData.status != xyzzy_Statuses.Stopped)
                {
                    activeGames++;
                    activePlayers += chatData.players.Count;
                }
            }
            Roboto.Settings.stats.logStat(new statItem("Active Games", this.GetType(), activeGames));
            Roboto.Settings.stats.logStat(new statItem("Active Players", this.GetType(), activePlayers));
            
            //sync packs where needed
            localdata.packSyncCheck();
            lo_bg.addone();

            //Handle background processing per chat (Timeouts / Throttle etc..)
            //create a temporary list of chatdata so we can pick the oldest X records
            List<mod_xyzzy_chatdata> dataToCheck = new List<mod_xyzzy_chatdata>();
            List<mod_xyzzy_chatdata> dataToMiniCheck = new List<mod_xyzzy_chatdata>();

            foreach (chat c in Roboto.Settings.chatData)
            {
                mod_xyzzy_chatdata chatData = (mod_xyzzy_chatdata)c.getPluginData<mod_xyzzy_chatdata>();
                if (chatData != null && chatData.status != xyzzy_Statuses.Stopped)
                { 
                    //do a full check at most once per day
                    if (chatData.statusCheckedTime < DateTime.Now.Subtract(new TimeSpan(1, 0, 0, 0)))
                    {
                        dataToCheck.Add(chatData);
                    }
                    //do a mini check at most every 5 mins
                    if (chatData.statusMiniCheckedTime < DateTime.Now.Subtract(new TimeSpan(0,0,5,0)))
                    {
                        dataToMiniCheck.Add(chatData);
                    }
                }
            }
            

            log("There are " + dataToCheck.Count() + " games to check. Checking oldest " + localdata.backgroundChatsToProcess , logging.loglevel.normal);
            lo_bg.totalLength = 5 + localdata.backgroundChatsToProcess + localdata.backgroundChatsToMiniProcess;

            //do a full check on the oldest 20 records. Dont check more than once per day. 
            bool firstrec = true;
            foreach (mod_xyzzy_chatdata chatData in dataToCheck.OrderBy(x => x.statusCheckedTime).Take(localdata.backgroundChatsToProcess))
            {
                if (firstrec)
                {
                    log("Oldest chat was last checked " + Convert.ToInt32(DateTime.Now.Subtract(chatData.statusCheckedTime).TotalMinutes) + " minute(s) ago", logging.loglevel.low);
                    Roboto.Settings.stats.logStat(new statItem("Background Wait", this.GetType(), Convert.ToInt32(DateTime.Now.Subtract(chatData.statusCheckedTime).TotalMinutes)));
                }
                chatData.check(true);
                firstrec = false;
                lo_bg.addone();
            }
            lo_bg.updateLongOp(localdata.backgroundChatsToProcess + 5);

            //also do a quick check on the oldest 100 ordered by statusMiniCheckTime
            log("There are " + dataToMiniCheck.Count() + " games to quick-check. Checking oldest " + localdata.backgroundChatsToMiniProcess, logging.loglevel.normal);
            firstrec = true;
            foreach (mod_xyzzy_chatdata chatData in dataToMiniCheck.OrderBy(x => x.statusMiniCheckedTime).Take(localdata.backgroundChatsToMiniProcess))
            {
                if (firstrec)
                {
                    log("Oldest chat was last quick-checked " + Convert.ToInt32(DateTime.Now.Subtract(chatData.statusMiniCheckedTime).TotalMinutes) + " minute(s) ago", logging.loglevel.low);
                    Roboto.Settings.stats.logStat(new statItem("Background Wait (Quickcheck)", this.GetType(), Convert.ToInt32(DateTime.Now.Subtract(chatData.statusMiniCheckedTime).TotalMinutes)));
                }
                chatData.check();
                firstrec = false;
                lo_bg.addone();
            }
            lo_bg.complete();
        }

        public override string getStats()
        {
            int activePlayers = 0;
            int activeGames = 0;
            int dormantGames = 0;

            mod_xyzzy_coredata localdata = (mod_xyzzy_coredata)getPluginData();

            foreach (chat c in Roboto.Settings.chatData)
            {
                mod_xyzzy_chatdata chatData = c.getPluginData<mod_xyzzy_chatdata>();
                if (chatData != null && chatData.status != xyzzy_Statuses.Stopped)
                {
                    activeGames++;
                    activePlayers += chatData.players.Count;
                    if (chatData.statusChangedTime < DateTime.Now.Subtract(new TimeSpan(30,0,0,0)) )
                    {
                        dormantGames++;
                    }
                }
            }

            log("There are " + dormantGames + " potentially cancelable games", logging.loglevel.normal);

            
            string result = activePlayers.ToString() + " players in " + activeGames.ToString() + " active games\n\r";
            result += localData.packs.Count().ToString() + " packs loaded containing " + (localData.questions.Count() + localData.answers.Count()) + " cards";

            return result;

        }

        public override bool replyReceived(ExpectedReply e, message m, bool messageFailed = false)
        {
            bool processed = false;
            chat c = null;

            //messages without a chat context, that we need to infer one from (e.g "leave" in a DM)
            if (e.messageData.Contains("PickGroup"))
            {
                //just drop out if its a cancel, the er should get closed off anyway. 
                if (m.text_msg == "Cancel") { return true; }

                //get the chat ID from the message and process further down
                try
                {
                    string chatID = m.text_msg.Substring(m.text_msg.LastIndexOf("(")+1);
                    chatID = chatID.Substring(0, chatID.Length - 1);
                    e.chatID = long.Parse(chatID);
                    c = Roboto.Settings.getChat(e.chatID);
                    if (c == null) { throw new DataMisalignedException("Couldnt find chat with that ID"); }
                }
                catch (Exception)
                {
                    log("A 'Pick Group' message could not be deciphered properly, no chat was found.", logging.loglevel.warn);
                    TelegramAPI.SendMessage(m.userID, "Sorry - something went wrong, I cant find that group.");
                    return (true);
                }
            }




            c = Roboto.Settings.getChat(e.chatID);
            if (c != null)
            {
                mod_xyzzy_chatdata chatData = (mod_xyzzy_chatdata)c.getPluginData(typeof(mod_xyzzy_chatdata), true);


                //did one of our outbound messages fail?
                if (messageFailed)
                {
                    //TODO - better handling of failed outbound messages. Timeout player or something depending on status? 
                    try
                    {
                        string message = "Failed Incoming expected reply";
                        if (c != null) { message += " for chat " + c.ToString(); }
                        if (m != null) { message += " received from chatID " + m.chatID + " from userID " + m.userID + " in reply to " + e.outboundMessageID; }


                        log(message, logging.loglevel.high);
                    }
                    catch (Exception ex)
                    {
                        log("Error thrown during failed reply processing " + ex.ToString(), logging.loglevel.critical);
                    }
                    return true;
                }

                else
                {
                    log("Incoming expected reply for chat " + c.ToString() + " received from chatID " + m.chatID + " from userID " + m.userID + " in reply to " + e.outboundMessageID, logging.loglevel.verbose);
                }





                //Set up the game, once we get a reply from the user. 
                if (e.messageData == "Settings")
                {
                    if (m.text_msg == "Cancel") { } //do nothing, should just end and go back
                    else if (m.text_msg == "Change Packs") { chatData.sendPackFilterMessage(m, 1); }
                    else if (m.text_msg == "Re-deal") { chatData.reDeal(); }
                    else if (m.text_msg == "Game Length") { chatData.askGameLength(m); }
                    else if (m.text_msg == "Extend") { chatData.extend(); }
                    else if (m.text_msg == "Reset") { chatData.reset(); }
                    else if (m.text_msg == "Force Question") { chatData.forceQuestion(); }
                    else if (m.text_msg == "Timeout") { chatData.askMaxTimeout(m.userID); }
                    else if (m.text_msg == "Delay") { chatData.askMinTimeout(m.userID); }
                    else if (m.text_msg == "Kick") { chatData.askKickMessage(m); }
                    else if (m.text_msg == "Mess With") { chatData.askFuckWithMessage(m); }
                    else if (m.text_msg == "Change Score") { chatData.askChangeScoreMessage(m); }
                    else if (m.text_msg == "Abandon")
                    {
                        TelegramAPI.GetExpectedReply(chatData.chatID, m.userID, "Are you sure you want to abandon the game?", true, typeof(mod_xyzzy), "Abandon", m.userFullName, -1, true, TelegramAPI.createKeyboard(new List<string>() { "Yes", "No" }, 2));
                    }
                    return true;
                }


                //TODO - think we can remove all of the checks here for if it is being called during the settings window. Should be handled by the neater code above? 

                else if (e.messageData == "useDefaults")
                {
                    if (m.text_msg == "Use Defaults")
                    {
                        //add all the q's and a's based on the previous settings / defaults if a new game. 
                        chatData.addQuestions();
                        chatData.addAllAnswers();
                        string keyboard = TelegramAPI.createKeyboard(new List<string> { "Start", "Cancel" }, 2);
                        TelegramAPI.GetExpectedReply(chatData.chatID, m.userID, "To start the game once enough players have joined click the \"Start\" button below. You will need three or more players to start the game.", true, typeof(mod_xyzzy), "Invites", m.userFullName, -1, true, keyboard);
                        chatData.setStatus(xyzzy_Statuses.Invites);
                    }
                    else if (m.text_msg == "Configure Game")
                    {
                        chatData.askGameLength(m);
                        chatData.setStatus(xyzzy_Statuses.SetGameLength);
                    }
                    else if (m.text_msg == "Cancel")
                    {

                        TelegramAPI.SendMessage(m.userID, "Cancelled setup");
                        chatData.setStatus(xyzzy_Statuses.Stopped);
                    }
                    else
                    {
                        string kb = TelegramAPI.createKeyboard(new List<string>() { "Use Defaults", "Configure Game", "Cancel" }, 2);
                        long messageID = TelegramAPI.GetExpectedReply(c.chatID, m.userID, "Not a valid answer. Do you want to start the game with the default settings, or set advanced optons first? You can change these options later with /xyzzy_settings", true, typeof(mod_xyzzy), "useDefaults", m.userFullName, -1, false, kb);
                    }
                    processed = true;
                }

                else if (e.messageData == "SetGameLength")
                {
                    int questions;
                    if (int.TryParse(m.text_msg, out questions) && questions >= -1)
                    {
                        //set the value
                        chatData.enteredQuestionCount = questions;

                        if (chatData.status == xyzzy_Statuses.SetGameLength)
                        {
                            //adding as part of the game setup
                            //next, ask which packs they want:
                            chatData.sendPackFilterMessage(m, 1);
                            chatData.setStatus(xyzzy_Statuses.setPackFilter);
                        }
                        else
                        {
                            //adding as part of a /settings. return to main
                            chatData.sendSettingsMessage(m);
                        }
                    }
                    else
                    {
                        TelegramAPI.GetExpectedReply(c.chatID, m.userID, m.text_msg + " is not a valid number. How many questions do you want the round to last for? -1 for infinite", true, typeof(mod_xyzzy), "SetGameLength");
                    }
                    processed = true;
                }


                if (e.messageData == "leaveGamePickGroup")
                {
                    chatData.removePlayer(m.userID);
                    processed = true;
                }




                //Set up the game filter, once we get a reply from the user. 
                else if (e.messageData.StartsWith("setPackFilter"))
                {
                    //figure out what page we are on. Should be in the message data
                    int currentPage = 1;
                    bool success = int.TryParse(e.messageData.Substring(14), out currentPage);
                    if (!success)
                    {
                        currentPage = 1;
                        log("Expected messagedata to contain a page number. Was " + e.messageData, logging.loglevel.high);
                    }
                    //import a cardcast pack
                    if (m.text_msg == "Import CardCast Pack")
                    {
                        TelegramAPI.GetExpectedReply(chatData.chatID, m.userID, Helpers.cardCast.boilerPlate + "\n\r"
                            + "To import a pack, enter the pack code. To cancel, type 'Cancel'", true, typeof(mod_xyzzy), "cardCastImport");
                        if (chatData.status == xyzzy_Statuses.setPackFilter) { chatData.setStatus(xyzzy_Statuses.cardCastImport); }
                    }
                    else if (m.text_msg == "Next")
                    {
                        currentPage++;
                        chatData.sendPackFilterMessage(m, currentPage);

                    }
                    else if (m.text_msg == "Prev")
                    {
                        currentPage--;
                        chatData.sendPackFilterMessage(m, currentPage);
                    }

                    //enable/disable an existing pack
                    else if (m.text_msg != "Continue")
                    {
                        chatData.processPackFilterMessage(m);
                        chatData.sendPackFilterMessage(m, currentPage);
                    }
                    //no packs selected, retry
                    else if (chatData.packFilterIDs.Count == 0)
                    {
                        chatData.sendPackFilterMessage(m, 1);
                    }
                    //This is presumably a continue now...
                    else
                    {
                        //are we adding this as part of the setup process?
                        if (chatData.status == xyzzy_Statuses.setPackFilter)
                        {
                            chatData.addQuestions();
                            chatData.addAllAnswers();

                            chatData.askMaxTimeout(m.userID);
                            chatData.setStatus(xyzzy_Statuses.setMaxHours);
                        }
                        else
                        {
                            //adding as part of a /settings. return to main
                            chatData.sendSettingsMessage(m);
                            //TelegramAPI.SendMessage(chatData.chatID, "Updated the pack list. New cards won't get added to the game until you restart, or /xyzzy_reDeal" );
                        }
                    }
                    processed = true;
                }


                //Cardcast importing
                else if (e.messageData == "cardCastImport")
                {
                    if (m.text_msg == "Cancel")
                    {
                        //return to plugins
                        chatData.sendPackFilterMessage(m, 1);
                        if (chatData.status == xyzzy_Statuses.cardCastImport) { chatData.setStatus(xyzzy_Statuses.setPackFilter); }
                    }
                    else
                    {
                        string importMessage;
                        Helpers.cardcast_pack pack = new Helpers.cardcast_pack();
                        bool success = localData.importCardCastPack(m.text_msg, out pack, out importMessage);
                        if (success == true)
                        {
                            //reply to user
                            TelegramAPI.SendMessage(m.userID, importMessage);
                            //enable the filter
                            chatData.processPackFilterMessage(m, pack.name);
                            //return to plugin selection
                            chatData.sendPackFilterMessage(m, 1);
                            if (chatData.status == xyzzy_Statuses.cardCastImport) { chatData.setStatus(xyzzy_Statuses.setPackFilter); }
                        }
                        else
                        {
                            TelegramAPI.GetExpectedReply(chatData.chatID, m.userID,
                            "Couldn't add the pack. " + importMessage + ". To import a pack, enter the pack code. To cancel, type 'Cancel'", true, typeof(mod_xyzzy), "cardCastImport");
                        }
                    }
                    processed = true;
                }

                //work out the maxWaitTime (timeout)
                else if (e.messageData == "setMaxHours")
                {
                    //try parse
                    bool success = chatData.setMaxTimeout(m.text_msg);
                    if (success && chatData.status == xyzzy_Statuses.setMaxHours) //could be at another status if being set mid-game
                    {
                        //move to the throttle
                        chatData.setStatus(xyzzy_Statuses.setMinHours);
                        chatData.askMinTimeout(m.userID);

                    }
                    else if (success)
                    {
                        //success, called inflite
                        //TelegramAPI.SendMessage(e.chatID, "Set timeouts to " + (chatData.maxWaitTimeHours == 0 ? "No Timeout" : chatData.maxWaitTimeHours.ToString() + " hours") );
                        //adding as part of a /settings. return to main
                        chatData.sendSettingsMessage(m);

                    }
                    else {
                        //send message, and retry
                        TelegramAPI.SendMessage(m.userID, "Not a valid value!");
                        chatData.askMaxTimeout(m.userID);
                    }
                    processed = true;
                }

                //work out the minWaitTime (throttle)
                else if (e.messageData == "setMinHours")
                {
                    //try parse
                    bool success = chatData.setMinTimeout(m.text_msg);
                    if (success && chatData.status == xyzzy_Statuses.setMinHours)//could be at another status if being set mid-game
                    {

                        //Ready to start game - tell the player they can start when they want
                        string keyboard = TelegramAPI.createKeyboard(new List<string> { "Start", "Cancel" }, 2);
                        TelegramAPI.GetExpectedReply(chatData.chatID, m.userID, "To start the game once enough players have joined click the \"Start\" button below. You will need three or more players to start the game.", true, typeof(mod_xyzzy), "Invites", m.userFullName, -1, true, keyboard);
                        chatData.setStatus(xyzzy_Statuses.Invites);

                    }
                    else if (success)
                    {
                        //adding as part of a /settings. return to main
                        chatData.sendSettingsMessage(m);
                        //success, called inflite
                        //TelegramAPI.SendMessage(e.chatID, (chatData.minWaitTimeHours == 0 ? "Game throttling disabled" :  "Set throttle to only allow one round every " + chatData.minWaitTimeHours.ToString() + " hours"));
                    }

                    else
                    {
                        //send message, and retry
                        TelegramAPI.SendMessage(m.userID, "Not a valid number!");
                        chatData.askMinTimeout(m.userID);
                    }
                    processed = true;
                }




                //start the game proper
                else if (chatData.status == xyzzy_Statuses.Invites && e.messageData == "Invites")
                // TBH, dont care what they reply with. Its probably "start" as thats whats on the keyboard, but lets not bother checking, 
                //as otherwise we would have to do some daft bounds checking 
                // && m.text_msg == "start")
                {
                    if (m.text_msg == "Cancel")
                    {
                        //allow player to cancel, otherwise the message just keeps coming back. 
                        chatData.setStatus(xyzzy_Statuses.Stopped);
                    }
                    else if (m.text_msg == "Override" && chatData.players.Count > 1)
                    {
                        log("Overriding player limit and starting game", logging.loglevel.high);
                        chatData.askQuestion(true);
                    }
                    else if (m.text_msg == "Start" && chatData.players.Count > 2)
                    {
                        log("Starting game", logging.loglevel.verbose);
                        chatData.askQuestion(true);
                    }
                    else if (m.text_msg == "Start")
                    {
                        string keyboard = TelegramAPI.createKeyboard(new List<string> { "Start", "Cancel" }, 2);
                        TelegramAPI.GetExpectedReply(chatData.chatID, m.userID, "Not enough players yet. You need three or more players to start the game. To start the game once enough players have joined click the \"Start\" button below.", true, typeof(mod_xyzzy), "Invites", m.userFullName, -1, true, keyboard);
                    }
                    else
                    {
                        string keyboard = TelegramAPI.createKeyboard(new List<string> { "Start", "Cancel" }, 2);
                        TelegramAPI.GetExpectedReply(chatData.chatID, m.userID, "To start the game once enough players have joined click the \"Start\" button below. You will need three or more players to start the game.", true, typeof(mod_xyzzy), "Invites", m.userFullName, -1, true, keyboard);
                    }

                    processed = true;
                }

                //A player answering the question
                else if (chatData.status == xyzzy_Statuses.Question && e.messageData == "Question")
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
                else if (chatData.status == xyzzy_Statuses.Judging && e.messageData == "Judging" && m != null)
                {
                    bool success = chatData.judgesResponse(m.text_msg);

                    processed = true;
                }


                //abandon game
                else if (e.messageData == "Abandon")
                {
                    chatData.setStatus(xyzzy_Statuses.Stopped);
                    Roboto.Settings.clearExpectedReplies(c.chatID, typeof(mod_xyzzy));
                    TelegramAPI.SendMessage(c.chatID, "Game abandoned. type /xyzzy_start to start a new game");
                    processed = true;
                }



                //kicking a player
                else if (e.messageData == "kick")
                {
                    mod_xyzzy_player p = chatData.getPlayer(m.text_msg);
                    if (p != null)
                    {
                        chatData.removePlayer(p.playerID);
                    }
                    chatData.check();
                    //now return to the last settings page
                    chatData.sendSettingsMessage(m);

                    processed = true;
                }

                else if (e.messageData == "fuckwith")
                {
                    mod_xyzzy_player p = chatData.getPlayer(m.text_msg);
                    if (p != null)
                    {
                        chatData.toggleFuckWith(p.playerID);
                    }
                    else
                    {
                        log("Couldnt find player " + m.text_msg, logging.loglevel.warn);
                        TelegramAPI.SendMessage(m.userID, "Couldnt find that player.");
                    }
                    chatData.check();
                    //now return to the last settings page
                    chatData.sendSettingsMessage(m);

                    processed = true;
                }

                else if (e.messageData == "changescore")
                {
                    mod_xyzzy_player p = chatData.getPlayer(m.text_msg);
                    if (p != null)
                    {
                        TelegramAPI.GetExpectedReply(chatData.chatID, m.userID, "What should their new score be?", true, typeof(mod_xyzzy), "changescorepoints " + p.playerID.ToString(), m.userFullName, -1, true, "", false, true, true);
                    }
                    else
                    {
                        log("Couldnt find player " + m.text_msg, logging.loglevel.warn);
                        TelegramAPI.SendMessage(m.userID, "Couldnt find that player.");
                        //now return to the last settings page
                        chatData.sendSettingsMessage(m);
                    }

                    processed = true;
                }

                //reply to the change points question
                else if (e.messageData.StartsWith("changescorepoints"))
                {
                    //find the player from the messagedata
                    long playerID = -1;
                    bool success = long.TryParse(e.messageData.Substring(18), out playerID);
                    if (success && playerID != -1)
                    {
                        //get the desired score
                        int playerScore = -1;
                        success = int.TryParse(m.text_msg, out playerScore);

                        //NB: we should only be able to get here as admin, so no need to check. 
                        success = chatData.setPlayerScore(playerID, playerScore);

                        if (!success)
                        {
                            log("Error changing points value", logging.loglevel.high);
                            TelegramAPI.SendMessage(m.userID, "Sorry, something went wrong.");
                            chatData.sendSettingsMessage(m);
                        }
                    }



                    chatData.check();


                    processed = true;
                }

            }



            else
            {
                log("Didnt process incoming expected reply! msg=" + e.text + " msgdata=" + e.messageData + " msg=" + m.text_msg, logging.loglevel.critical);
            }

            return processed;
        }


        public override void sampleData()
        {
            log("Adding stub sample packs");

            //Add packs for the standard CaH packs. These should be synced when we do startupChecks()
            Helpers.cardcast_pack primaryPack = new Helpers.cardcast_pack("Cards Against Humanity", "CAHBS", "Cards Against Humanity");
            primaryPack.overrideGUID(mod_xyzzy.primaryPackID); //override this one's guid so we can add it by default to new poacks. 

            localData.packs.Add(primaryPack);
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
            logging.longOp lo_s = new logging.longOp("XYZZY - Startup Checks", 5);
            //check that our primary pack has the correct guid
            //does it exist? 
            if (localData.getPack(primaryPackID) != null)
            {
                log("OK - Primary pack exists", logging.loglevel.verbose);
            }
            else
            {
                Helpers.cardcast_pack primaryPack = localData.getPack("Cards Against Humanity");
                if (primaryPack != null)
                {
                    //swap all guids over to the correct one
                    Guid masterID = primaryPack.packID;

                    foreach(mod_xyzzy_card qc in localData.questions.Where(x => x.packID == masterID )) { qc.packID = primaryPackID; log(qc.ToString() + " has been shuffled", logging.loglevel.high); }
                    foreach (mod_xyzzy_card qa in localData.answers.Where(x => x.packID == masterID)) { qa.packID = primaryPackID; log(qa.ToString() + " has been shuffled", logging.loglevel.high); }

                    //swap any filters
                    foreach (chat c in Roboto.Settings.chatData)
                    {
                        mod_xyzzy_chatdata cd = c.getPluginData<mod_xyzzy_chatdata>();
                        if (cd != null)
                        {
                            int filtercopies = cd.packFilterIDs.RemoveAll(x => x == masterID);
                            if (filtercopies > 0)
                            {
                                log("Chat " + c.ToString() + " filter updated with correct guid", logging.loglevel.warn);
                                cd.packFilterIDs.Add(primaryPackID);
                            }
                        }

                    }

                    //replace the pack ID
                    primaryPack.packID = primaryPackID;

                }
                else
                {
                    log("No copy of the primary CAH pack could be found!", logging.loglevel.critical);
                }
            }


            //check through our chatData and log some stats
            lo_s.totalLength = Roboto.Settings.chatData.Count();
            log("XYZZY Chatdata:", logging.loglevel.verbose);
            log("ChatID\tstatus\tStatus Changed On\tplayers\tfilters:", logging.loglevel.verbose);
            foreach (chat c in Roboto.Settings.chatData)
            {

                mod_xyzzy_chatdata cd = (mod_xyzzy_chatdata)c.getPluginData(typeof(mod_xyzzy_chatdata));
                if (cd != null)
                {
                    log(cd.chatID.ToString().PadRight(15," ".ToCharArray()[0]) 
                        + "\t" + cd.status.ToString().PadRight(22, " ".ToCharArray()[0]) 
                        + "\t" + cd.statusChangedTime 
                        + "\t" + cd.players.Count() 
                        + "\t" + cd.packFilterIDs.Count, logging.loglevel.verbose);
                    
                }
                lo_s.addone();
            }

            //make sure our local pack filter list is fully populated 
            //MOVED TO Settings.Validate localData.startupChecks();

            
            //todo - this should be a general pack remove option
            //DATAFIX: rename & replace any "good" packs from when they were manually loaded.
            //foreach (mod_xyzzy_card q in localData.questions.Where(x => x.category == " Image1").ToList()) { q.category = "Image1"; }
            //foreach (mod_xyzzy_card a in localData.answers.Where(x => x.category == " Image1").ToList()) { a.category = "Image1"; }
            //localData.packs.RemoveAll(x => x.name == " Image1");
            //not required now. Need to do a proper migration script at some point for merging / deleting packs. 
            //foreach (chat c in Roboto.Settings.chatData)
            //{
            //    mod_xyzzy_chatdata chatData = (mod_xyzzy_chatdata)c.getPluginData(typeof(mod_xyzzy_chatdata));
            //    if (chatData != null)
            //    {
            //        //if (chatData.packFilter.Contains("Base") || chatData.packFilter.Contains(" Base")) { chatData.packFilter.Add("Cards Against Humanity"); }
            //        //if (chatData.packFilter.Contains("CAHe1") || chatData.packFilter.Contains(" CAHe1")) { chatData.packFilter.Add("Expansion 1 - CAH"); }
            //        //if (chatData.packFilter.Contains("CAHe2") || chatData.packFilter.Contains(" CAHe2")) { chatData.packFilter.Add("Expansion 2 - CAH"); }
            //        //if (chatData.packFilter.Contains("CAHe3") || chatData.packFilter.Contains(" CAHe3")) { chatData.packFilter.Add("Expansion 3 - CAH"); }
            //        //if (chatData.packFilter.Contains("CAHe4") || chatData.packFilter.Contains(" CAHe4")) { chatData.packFilter.Add("Expansion 4 - CAH"); }
            //        //if (chatData.packFilter.Contains("CAHe5") || chatData.packFilter.Contains(" CAHe5")) { chatData.packFilter.Add("CAH Fifth Expansion"); }
            //        //if (chatData.packFilter.Contains("CAHe6") || chatData.packFilter.Contains(" CAHe6")) { chatData.packFilter.Add("CAH Sixth Expansion"); }
            //        if (chatData.packFilter.Contains(" Image1")) { chatData.packFilter.Add("Image1"); }

            //        chatData.packFilter.RemoveAll(x => x == " Image1");
            //        //chatData.packFilter.RemoveAll(x => x.Trim() == "Base");
            //        //chatData.packFilter.RemoveAll(x => x.Trim() == "CAHe1");
            //        //chatData.packFilter.RemoveAll(x => x.Trim() == "CAHe2");
            //        //chatData.packFilter.RemoveAll(x => x.Trim() == "CAHe3");
            //        //chatData.packFilter.RemoveAll(x => x.Trim() == "CAHe4");
            //        //chatData.packFilter.RemoveAll(x => x.Trim() == "CAHe5");
            //        //chatData.packFilter.RemoveAll(x => x.Trim() == "CAHe6");
            //    }
            //}


            //MOVED - done as part of regular background sync
            //localData.packSyncCheck();

            //Check for null-IDd packs and report
            //TODO - move to coredata
            List<Helpers.cardcast_pack> nullIDPacks = localData.packs.Where(x => string.IsNullOrEmpty(x.packCode)).ToList();
            Roboto.log.log("There are " + nullIDPacks.Count() + " packs without pack codes." +
                (nullIDPacks.Count() == 0 ? "": " Try rename an existing pack in the XML file to the same name, or add the pack code to this pack - should merge in next time a Sync is called. " )
                , nullIDPacks.Count() > 0?logging.loglevel.critical:  logging.loglevel.normal);
            foreach (Helpers.cardcast_pack pack in nullIDPacks)
            {
                Roboto.log.log("Pack " + pack.name + " has no pack code ", logging.loglevel.critical);
            }
            lo_s.complete();
            
            
        }

    }
}
