using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace RobotoChatBot.Modules
{


    /// <summary>
    /// The XXZZY Plugin
    /// </summary>
    public class mod_xyzzy : RobotoModuleTemplate
    {
        public static Guid primaryPackID = new Guid("FACEBABE-DEAD-BEEF-ABBA-FACEBABEFADE");
        public static Guid AllPacksEnabledID = Guid.Empty;

        /// <summary>
        /// Provide a handier way of accessing the plugin data stored in the template. 
        /// </summary>
        private mod_xyzzy_coredata localPluginData
        {
            get { return (mod_xyzzy_coredata)localData;  }
            set { localData = value; }
        }

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
                    log("Chatdata doesnt exist!", logging.loglevel.critical);
                    
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
                    
                    long messageID = Messaging.SendQuestion(c.chatID, m.userID, "Do you want to start the game with the default settings, or set advanced optons first? You can change these options later with /xyzzy_settings", true, typeof(mod_xyzzy), "useDefaults", m.userFullName, -1,false,kb);

                    if (messageID == long.MinValue)
                    {
                        //no private message session
                        Messaging.SendMessage(m.chatID, m.userFullName + " needs to open a private chat to @" +
                            Roboto.Settings.botUserName + " to be able to start a game", m.userFullName, false, -1, true);
                    }
                    else
                    {
                        //message went out successfully, start setting it up proper

                        chatData.setStatus(xyzzy_Statuses.useDefaults);
                        //add the player that started the game
                        chatData.addPlayer(new mod_xyzzy_player(m.userFullName, m.userHandle, m.userID));

                        //send out invites
                        Messaging.SendMessage(m.chatID, m.userFullName + " is starting a new game of xyzzy! Type /xyzzy_join to join. You can join / leave " +
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
                        i = Messaging.SendMessage(m.userID, "You joined the xyzzy game in " + m.chatName);
                        if (i == -1)
                        {
                            log("Adding user, but the outbound message is still queued", logging.loglevel.verbose);
                            Messaging.SendMessage(m.chatID, "Sent " + m.userFullName + " a message, but I'm waiting for them to reply to another question. "
                                + m.userFullName + " is in the game, but will need to clear their other PMs before they see any questions. ", m.userFullName, false, m.message_id);

                        }
                        else if (i < 0)
                        {
                            log("Adding user, but message blocked, abandoning", logging.loglevel.warn);
                            Messaging.SendMessage(m.chatID, "Couldn't add " + m.userFullName + " to the game, as I couldnt send them a message. "
                               + m.userFullName + " probably needs to open a chat session with me. "
                               + "Create a message session, then try /xyzzy_join again.", m.userFullName, false, m.message_id);
                        }

                    
                        if (i != long.MinValue) //if we didnt get an error sending the message
                        {
                            log("Adding user processing", logging.loglevel.verbose);
                            bool added = chatData.addPlayer(new mod_xyzzy_player(m.userFullName, m.userHandle, m.userID));
                            if (added) { Messaging.SendMessage(c.chatID, m.userFullName + " has joined the game"); }
                            else { Messaging.SendMessage(c.chatID, m.userFullName + " is already in the game"); }
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
                    //if (removed) { Messaging.SendMessage(c.chatID, m.userFullName + " has left the game"); }
                    //else { Messaging.SendMessage(c.chatID, m.userFullName + " isnt part of the game, and can't be removed!"); }
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
                    Messaging.SendMessage(m.chatID, response, false, m.message_id);
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
                    Messaging.SendMessage(m.chatID, "Resetting everyone's cards, and shuffled the decks", false, m.message_id);
                    chatData.reDeal();
                    
                    processed = true;
                }

                else if (m.text_msg.StartsWith("/xyzzy_reset"))
                {
                    chatData.resetScores();
                    Messaging.SendMessage(m.chatID, "Scores have been reset!", false, m.message_id);
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
                        long messageID = Messaging.SendQuestion(0, m.userID, "Which game would you like to leave?", true, typeof(mod_xyzzy), "leaveGamePickGroup", m.userFullName, -1, false, kb, false, false, true);
                    }
                    else
                    {
                        Messaging.SendMessage(m.userID, "You are not in any active games.", null, false, m.message_id, true, true);
                    }


                    processed = true;
                }


                else if (m.text_msg.StartsWith("/xyzzy_"))
                {
                    Messaging.SendMessage(m.chatID, "To start a game, add me to a group chat, and type /xyzzy_start");
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
                if (chatData != null)
                { 
                    //do a full check (incl. getting group count to check access) at most once per day. Dont full check stopped games
                    if (chatData.statusCheckedTime < DateTime.Now.Subtract(new TimeSpan(1, 0, 0, 0)) && chatData.status != xyzzy_Statuses.Stopped)
                    {
                        dataToCheck.Add(chatData);
                    }
                    //do a mini check on active games at most every 15 mins
                    else if (chatData.statusMiniCheckedTime < DateTime.Now.Subtract(new TimeSpan(0,0,15,0)))
                    {
                        dataToMiniCheck.Add(chatData);
                    }
                }
            }
            

            log("There are " + dataToCheck.Count() + " games to check. Checking oldest " + localdata.backgroundChatsToProcess , logging.loglevel.normal);
            lo_bg.totalLength = 5 + localdata.backgroundChatsToProcess + localdata.backgroundChatsToMiniProcess;

            //do a full check on the oldest n records. Dont check more than once per day. 
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

            //also do a quick check on the oldest x ordered by statusMiniCheckTime
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

            //check if we need to remove any dormant packs
            localdata.removeDormantPacks();
            lo_bg.addone();

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
            result += localPluginData.packs.Count().ToString() + " packs loaded containing " + (localPluginData.questions.Count() + localPluginData.answers.Count()) + " cards";

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
                    c = Chats.getChat(e.chatID);
                    if (c == null) { throw new DataMisalignedException("Couldnt find chat with that ID"); }
                }
                catch (Exception)
                {
                    log("A 'Pick Group' message could not be deciphered properly, no chat was found.", logging.loglevel.warn);
                    Messaging.SendMessage(m.userID, "Sorry - something went wrong, I cant find that group.");
                    return (true);
                }
            }




            c = Chats.getChat(e.chatID);
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
                        Messaging.SendQuestion(chatData.chatID, m.userID, "Are you sure you want to abandon the game?", true, typeof(mod_xyzzy), "Abandon", m.userFullName, -1, true, TelegramAPI.createKeyboard(new List<string>() { "Yes", "No" }, 2));
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
                        Messaging.SendQuestion(chatData.chatID, m.userID, "To start the game once enough players have joined click the \"Start\" button below. You will need three or more players to start the game.", true, typeof(mod_xyzzy), "Invites", m.userFullName, -1, true, keyboard);
                        chatData.setStatus(xyzzy_Statuses.Invites);
                    }
                    else if (m.text_msg == "Configure Game")
                    {
                        chatData.askGameLength(m);
                        chatData.setStatus(xyzzy_Statuses.SetGameLength);
                    }
                    else if (m.text_msg == "Cancel")
                    {

                        Messaging.SendMessage(m.userID, "Cancelled setup");
                        chatData.setStatus(xyzzy_Statuses.Stopped);
                    }
                    else
                    {
                        string kb = TelegramAPI.createKeyboard(new List<string>() { "Use Defaults", "Configure Game", "Cancel" }, 2);
                        long messageID = Messaging.SendQuestion(c.chatID, m.userID, "Not a valid answer. Do you want to start the game with the default settings, or set advanced optons first? You can change these options later with /xyzzy_settings", true, typeof(mod_xyzzy), "useDefaults", m.userFullName, -1, false, kb);
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
                        Messaging.SendQuestion(c.chatID, m.userID, m.text_msg + " is not a valid number. How many questions do you want the round to last for? -1 for infinite", true, typeof(mod_xyzzy), "SetGameLength");
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
                        Messaging.SendQuestion(chatData.chatID, m.userID, Helpers.cardCast.boilerPlate + "\n\r"
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
                            //Messaging.SendMessage(chatData.chatID, "Updated the pack list. New cards won't get added to the game until you restart, or /xyzzy_reDeal" );
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
                        bool success = localPluginData.importCardCastPack(m.text_msg, out pack, out importMessage);
                        if (success == true)
                        {
                            //reply to user
                            Messaging.SendMessage(m.userID, importMessage);
                            //enable the filter
                            chatData.processPackFilterMessage(m, pack.name);
                            //return to plugin selection
                            chatData.sendPackFilterMessage(m, 1);
                            if (chatData.status == xyzzy_Statuses.cardCastImport) { chatData.setStatus(xyzzy_Statuses.setPackFilter); }
                        }
                        else
                        {
                            Messaging.SendQuestion(chatData.chatID, m.userID,
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
                        //Messaging.SendMessage(e.chatID, "Set timeouts to " + (chatData.maxWaitTimeHours == 0 ? "No Timeout" : chatData.maxWaitTimeHours.ToString() + " hours") );
                        //adding as part of a /settings. return to main
                        chatData.sendSettingsMessage(m);

                    }
                    else {
                        //send message, and retry
                        Messaging.SendMessage(m.userID, "Not a valid value!");
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
                        Messaging.SendQuestion(chatData.chatID, m.userID, "To start the game once enough players have joined click the \"Start\" button below. You will need three or more players to start the game.", true, typeof(mod_xyzzy), "Invites", m.userFullName, -1, true, keyboard);
                        chatData.setStatus(xyzzy_Statuses.Invites);

                    }
                    else if (success)
                    {
                        //adding as part of a /settings. return to main
                        chatData.sendSettingsMessage(m);
                        //success, called inflite
                        //Messaging.SendMessage(e.chatID, (chatData.minWaitTimeHours == 0 ? "Game throttling disabled" :  "Set throttle to only allow one round every " + chatData.minWaitTimeHours.ToString() + " hours"));
                    }

                    else
                    {
                        //send message, and retry
                        Messaging.SendMessage(m.userID, "Not a valid number!");
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
                        Messaging.SendQuestion(chatData.chatID, m.userID, "Not enough players yet. You need three or more players to start the game. To start the game once enough players have joined click the \"Start\" button below.", true, typeof(mod_xyzzy), "Invites", m.userFullName, -1, true, keyboard);
                    }
                    else
                    {
                        string keyboard = TelegramAPI.createKeyboard(new List<string> { "Start", "Cancel" }, 2);
                        Messaging.SendQuestion(chatData.chatID, m.userID, "To start the game once enough players have joined click the \"Start\" button below. You will need three or more players to start the game.", true, typeof(mod_xyzzy), "Invites", m.userFullName, -1, true, keyboard);
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
                    Messaging.clearExpectedReplies(c.chatID, typeof(mod_xyzzy));
                    Messaging.SendMessage(c.chatID, "Game abandoned. type /xyzzy_start to start a new game");
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
                        Messaging.SendMessage(m.userID, "Couldnt find that player.");
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
                        Messaging.SendQuestion(chatData.chatID, m.userID, "What should their new score be?", true, typeof(mod_xyzzy), "changescorepoints " + p.playerID.ToString(), m.userFullName, -1, true, "", false, true, true);
                    }
                    else
                    {
                        log("Couldnt find player " + m.text_msg, logging.loglevel.warn);
                        Messaging.SendMessage(m.userID, "Couldnt find that player.");
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
                            Messaging.SendMessage(m.userID, "Sorry, something went wrong.");
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




        /// <summary>
        /// Startup checks and housekeeping
        /// </summary>
        public override void startupChecks()
        {

            Roboto.Settings.stats.registerStatType("New Games Started", this.GetType(), System.Drawing.Color.Aqua);
            Roboto.Settings.stats.registerStatType("Games Ended", this.GetType(), System.Drawing.Color.Orange);
            Roboto.Settings.stats.registerStatType("Hands Played", this.GetType(), System.Drawing.Color.Olive);
            Roboto.Settings.stats.registerStatType("Packs Synced", this.GetType(), System.Drawing.Color.DarkBlue);
            Roboto.Settings.stats.registerStatType("Packs Total", this.GetType(), System.Drawing.Color.LawnGreen, stats.displaymode.line, stats.statmode.absolute);
            Roboto.Settings.stats.registerStatType("Dormant Packs Removed", this.GetType(), System.Drawing.Color.Bisque);
            Roboto.Settings.stats.registerStatType("Dormant Packs Total", this.GetType(), System.Drawing.Color.CadetBlue, stats.displaymode.line, stats.statmode.absolute);
            Roboto.Settings.stats.registerStatType("Bad Responses", this.GetType(), System.Drawing.Color.Olive);
            Roboto.Settings.stats.registerStatType("Active Games", this.GetType(), System.Drawing.Color.Green, stats.displaymode.line, stats.statmode.absolute);
            Roboto.Settings.stats.registerStatType("Active Players", this.GetType(), System.Drawing.Color.Blue, stats.displaymode.line, stats.statmode.absolute);
            Roboto.Settings.stats.registerStatType("Background Wait", this.GetType(), System.Drawing.Color.Red, stats.displaymode.line, stats.statmode.absolute);
            Roboto.Settings.stats.registerStatType("Background Wait (Quickcheck)", this.GetType(), System.Drawing.Color.Red, stats.displaymode.line, stats.statmode.absolute);
            Roboto.Settings.stats.registerStatType("Background Wait (Pack Sync)", this.GetType(), System.Drawing.Color.Cyan, stats.displaymode.line, stats.statmode.absolute);

            Console.WriteLine(localPluginData.questions.Count.ToString() + " questions and " + localPluginData.answers.Count.ToString() + " answers loaded for xyzzy");

            //logging.longOp lo_s = new logging.longOp("XYZZY - Startup Checks", 5);
            

            /*TODO - move this somewhere else, dumping the chat list is daft
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
            */
            //lo_s.complete();
            
            
        }

    }
}
