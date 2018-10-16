using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace RobotoChatBot.Modules
{
    [XmlType("mod_standard_data")]
    [Serializable]
    public class mod_standard_data : RobotoModuleDataTemplate
    {
        public DateTime lastSaveToDiskDateTime = DateTime.Now;
    }

    [XmlType("mod_standard_chatdata")]
    [Serializable]
    public class mod_standard_chatdata : RobotoModuleChatDataTemplate
    {
        //Timespan won't serialise, so need to use a backing "long" to store the actual value. 
        public long x_quietHoursStartTime = TimeSpan.MinValue.Ticks;
        public long x_quietHoursEndTime = TimeSpan.MinValue.Ticks;

        [XmlIgnore]
        public TimeSpan quietHoursStartTime
        {
            get
            {
                try
                {
                    return new TimeSpan(x_quietHoursStartTime);
                }
                catch (NullReferenceException)
                {
                    x_quietHoursStartTime = TimeSpan.MinValue.Ticks;
                    return new TimeSpan(x_quietHoursStartTime);
                }
            }
            set { x_quietHoursStartTime = value.Ticks; }

        }
        [XmlIgnore]
        public TimeSpan quietHoursEndTime
        {
            get
            {
                try
                {
                    return new TimeSpan(x_quietHoursEndTime);
                }
                catch (NullReferenceException)
                {
                    x_quietHoursEndTime = TimeSpan.MinValue.Ticks;
                    return new TimeSpan(x_quietHoursEndTime);
                }
            }
            set { x_quietHoursEndTime = value.Ticks; }
        }

    }

    public class mod_standard : RobotoModuleTemplate
    {
        private mod_standard_data localData;

        public override void init()
        {
            pluginDataType = null;

            chatHook = true;
            chatEvenIfAlreadyMatched = false;
            chatIfMuted = true;
            chatPriority = 1;


            pluginDataType = typeof(mod_standard_data);
            pluginChatDataType = typeof(mod_standard_chatdata);

            backgroundHook = true;
            backgroundMins = 5;
            

        }

        public override void initData()
        {
            try
            {
                //TODO - should move away from needing this local object. 
                localData = Roboto.Settings.getPluginData<mod_standard_data>();
            }
            catch (InvalidDataException)
            {
                //Data doesnt exist, create, populate with sample data and register for saving
                localData = new mod_standard_data();
                sampleData();
                Roboto.Settings.registerData(localData);
            }
        }

        public override void initChatData(chat c)
        {
            mod_standard_chatdata chatData = c.getPluginData<mod_standard_chatdata>();

            if (chatData == null)
            {
                //Data doesnt exist, create, populate with sample data and register for saving
                chatData = new mod_standard_chatdata();
                c.addChatData(chatData);
            }
        }

        public override string getMethodDescriptions()
        {
            return
                "help - Returns this list of commands" + "\n\r" +
                "start - Starts listening to the chat" + "\n\r" +
                "stop - Stops listening to the chat, until a START is entered." + "\n\r" +
                "save - Saves any outstanding in memory stuff to disk." + "\n\r" +
                "stats - Returns an overview of the currently loaded plugins." + "\n\r" +
                "setquiethours - Sets quiet hours for the chat." + "\n\r" +
                "addadmin - adds an chat administrator" + "\n\r" +
                "removeadmin - removes a chat administrator"
                ;
        }

        public override string getWelcomeDescriptions()
        {
            return null; //deliberately don't return anything here - it shouldnt show up in the welcome message

        }

        public static String getAllMethodDescriptions()
        {
            String methods = "The following commands are available:";
            foreach (RobotoModuleTemplate plugin in settings.plugins)
            {
                methods += "\n\r" + plugin.getMethodDescriptions(); 
            }
            return methods;
        }

        /// <summary>
        /// Get basic stats
        /// </summary>
        /// <returns></returns>
        public override string getStats()
        {
            return "There are " + Roboto.Settings.expectedReplies.Count() + " messages awaiting reply.";
        }

        /// <summary>
        /// Background processing for Roboto
        /// </summary>
        protected override void backgroundProcessing()
        {
            //do we need to save? 
            if (localData.lastSaveToDiskDateTime.AddMinutes(Roboto.Settings.saveXMLeveryXMins) < DateTime.Now)
            {
                localData.lastSaveToDiskDateTime = DateTime.Now;
                Roboto.Settings.save();
            }

            //do general housekeeping
            Roboto.Settings.stats.houseKeeping();
            Roboto.Settings.expectedReplyBackgroundProcessing();
            

        }

        public override bool chatEvent(message m, chat c = null)
        {
            bool processed = false;

            if (m.text_msg.StartsWith("/help") && c != null && c.muted == false)
            {

                mod_standard_chatdata chatData = c.getPluginData<mod_standard_chatdata>();
                string openingMessage = "This is chat " + (c.chatTitle == null ? "" : c.chatTitle) + " (" + c.chatID + "). " +"\n\r";
                if (chatData.quietHoursStartTime != TimeSpan.MinValue && chatData.quietHoursEndTime != TimeSpan.MinValue)
                {
                    openingMessage += "Quiet time set between " + chatData.quietHoursStartTime.ToString("c") + " and " + chatData.quietHoursEndTime.ToString("c") + ". \n\r";
                }

                TelegramAPI.SendMessage(m.chatID, openingMessage +  getAllMethodDescriptions());
                processed = true;
            }
            else if (m.text_msg.StartsWith("/save"))
            {
                Roboto.Settings.save();
                TelegramAPI.SendMessage(m.chatID, "Saved settings");
                processed = true;
            }
            else if (m.text_msg.StartsWith("/stop") && c != null)
            {
                c.muted = true;
                TelegramAPI.SendMessage(m.chatID, "I am now ignoring all messages in this chat until I get a /start command. ");
                //TODO - make sure we abandon any games

                processed = true;
            }
            else if (m.text_msg.StartsWith("/start") && c != null && c.muted == true)
            {
                c.muted = false;
                TelegramAPI.SendMessage(m.chatID, "I am listening for messages again. Type /help for a list of commands." + "\n\r" + getAllWelcomeDescriptions());
                processed = true;
            }
            else if (m.text_msg.StartsWith("/start"))
            {
                //a default /start message where we arent on pause. Might be in group or private chat. 
                TelegramAPI.SendMessage(m.chatID, getAllWelcomeDescriptions());

            }


            else if (m.text_msg.StartsWith("/background"))
            {
                //kick off the background loop. 
                Roboto.Settings.backgroundProcessing(true);
            }

            else if (m.text_msg.StartsWith("/setquiethours") && c != null)
            {
                TelegramAPI.GetExpectedReply(m.chatID, m.userID, "Enter the start time for the quiet hours, cancel, or disable. This should be in the format hh:mm:ss (e.g. 23:00:00)", true, this.GetType(), "setQuietHours");
                processed = true;
            }

            else if (m.text_msg.StartsWith("/stats"))
            {
                TimeSpan uptime = DateTime.Now.Subtract(Roboto.startTime);

                String statstxt = "I is *@" + Roboto.Settings.botUserName + "*" + "\n\r" +
                    "Uptime: " + uptime.Days.ToString() + " days, " + uptime.Hours.ToString() + " hours and " + uptime.Minutes.ToString() + " minutes." + "\n\r" +
                    "I currently know about " + Roboto.Settings.chatData.Count().ToString() + " chats." + "\n\r" +
                    "The following plugins are currently loaded:" + "\n\r";

                foreach (RobotoModuleTemplate plugin in settings.plugins)
                {
                    statstxt += "*" + plugin.GetType().ToString() + "*" + "\n\r";
                    statstxt += plugin.getStats() + "\n\r";
                }

                TelegramAPI.SendMessage(m.chatID, statstxt, m.userFullName, true);
                processed = true;
            }
            else if (m.text_msg.StartsWith("/addadmin") && c != null)
            {
                //check if we have privs. This will send a fail if not.
                if (c.checkAdminPrivs(m.userID, c.chatID))
                {
                    //if there is no admin, add player
                    if (!c.chatHasAdmins())
                    {
                        bool added = c.addAdmin(m.userID, m.userID);
                        if (added)
                        {
                            TelegramAPI.SendMessage(m.chatID, "Added " + m.userFullName + " as admin.");
                        }
                        else
                        {
                            TelegramAPI.SendMessage(m.chatID, "Something went wrong! ");
                            log("Error adding user as an admin", logging.loglevel.high);
                        }
                    }
                    else
                    {
                        //create a keyboard with the recent chat members
                        List<string> members = new List<string>();
                        foreach (chatPresence p in c.getRecentChatUsers()) { members.Add(p.ToString()); }
                        //send keyboard to player requesting admin. 
                        TelegramAPI.GetExpectedReply(m.chatID, m.userID, "Who do you want to add as admin?", true, typeof(mod_standard), "ADDADMIN", m.userFullName, -1, false, TelegramAPI.createKeyboard(members, 2));
                    }

                }
                else
                {
                    log("User tried to add admin, but insufficient privs", logging.loglevel.high);
                }
                processed = true;
            }
            else if (m.text_msg.StartsWith("/removeadmin") && c != null)
            {
                //check if we have privs. This will send a fail if not.
                if (c.checkAdminPrivs(m.userID, c.chatID))
                {
                    //if there is no admin, add player
                    if (!c.chatHasAdmins())
                    {
                        TelegramAPI.SendMessage(m.chatID, "Group currently doesnt have any admins!");
                    }
                    else
                    {
                        //create a keyboard with the recent chat members
                        List<string> members = new List<string>();
                        foreach (long userID in c.chatAdmins) { members.Add(userID.ToString()); }
                        //send keyboard to player requesting admin. 
                        TelegramAPI.GetExpectedReply(m.chatID, m.userID, "Who do you want to remove as admin?", true, typeof(mod_standard), "REMOVEADMIN", m.userFullName, -1, false, TelegramAPI.createKeyboard(members, 2));
                    }

                }
                else
                {
                    log("User tried to remove admin, but insufficient privs", logging.loglevel.high);
                }
                processed = true;
            }




            else if (m.text_msg.StartsWith("/statgraph"))
            {
                string[] argsList = m.text_msg.Split(" ".ToCharArray(), 2);
                Stream image;
                //Work out args and get our image
                if (argsList.Length > 1)
                {
                    string args = argsList[1];
                    image = Roboto.Settings.stats.generateImage(argsList[1].Split("|"[0]).ToList());
                }
                else
                {
                    image = Roboto.Settings.stats.generateImage(new List<string>());
                }

                //Sending image...
                if (image != null)
                {
                    TelegramAPI.SendPhoto(m.chatID, "Stats", image, "StatsGraph.jpg", "application/octet-stream", m.message_id, false);
                }
                else
                {
                    TelegramAPI.SendMessage(m.chatID, "No statistics were found that matched your input, sorry!");
                }
                processed = true;

                //TODO - keyboard for stats?
            }

                return processed;
        }

        public string getAllWelcomeDescriptions()
        {
            {
                String description = "Welcome to " + Roboto.Settings.botUserName + ".";
                foreach (RobotoModuleTemplate plugin in settings.plugins)
                {
                    string moduleDesc = plugin.getWelcomeDescriptions();
                    if (moduleDesc != null) { description += "\n\r" + moduleDesc; }
                }
                return description;
            }
        }
        public override bool replyReceived(ExpectedReply e, message m, bool messageFailed = false)
        {
            chat c = Roboto.Settings.getChat(e.chatID);
            mod_standard_chatdata chatData = c.getPluginData<mod_standard_chatdata>();

            if (e.messageData == "setQuietHours")
            {
                if (m.text_msg.ToLower() == "cancel")
                {
                    //dont need to do anything else
                }
                else if (m.text_msg.ToLower() == "disable")
                {
                    chatData.quietHoursEndTime = TimeSpan.MinValue;
                    chatData.quietHoursStartTime = TimeSpan.MinValue;
                    TelegramAPI.SendMessage(e.chatID, "Quiet hours have been disabled");
                }
                else
                {
                    //try parse it 
                    TimeSpan s;
                    bool success = TimeSpan.TryParse(m.text_msg, out s);
                    if (success && s > TimeSpan.Zero && s.TotalDays < 1)
                    {
                        chatData.quietHoursStartTime = s;
                        TelegramAPI.GetExpectedReply(e.chatID, m.userID, "Enter the wake time for the quiet hours, cancel, or disable. This should be in the format hh:mm:ss (e.g. 23:00:00)", true, this.GetType(), "setWakeHours", m.userFullName, -1, false, "", false, false, true);
                    }
                    else
                    {
                        TelegramAPI.GetExpectedReply(e.chatID, m.userID,  "Invalid value. Enter the start time for the quiet hours, cancel, or disable. This should be in the format hh:mm:ss (e.g. 23:00:00)", true, this.GetType(), "setQuietHours", m.userFullName, -1, false, "", false, false, true);
                    }


                }
                return true;

            }
            else if (e.messageData == "setWakeHours")
            {
                if (m.text_msg.ToLower() == "cancel")
                {
                    //dont need to do anything else
                }
                else if (m.text_msg.ToLower() == "disable")
                {
                    chatData.quietHoursEndTime = TimeSpan.MinValue;
                    chatData.quietHoursStartTime = TimeSpan.MinValue;
                    TelegramAPI.SendMessage(e.chatID, "Quiet hours have been disabled");
                }
                else
                {
                    //try parse it 
                    TimeSpan s;
                    bool success = TimeSpan.TryParse(m.text_msg, out s);
                    if (success && s > TimeSpan.Zero && s.TotalDays < 1)
                    {
                        chatData.quietHoursEndTime = s;
                        TelegramAPI.SendMessage(e.chatID, "Quiet time set from " + chatData.quietHoursStartTime.ToString("c") + " to " + chatData.quietHoursEndTime.ToString("c"));   
                    }
                    else
                    {
                        TelegramAPI.GetExpectedReply(e.chatID, m.userID,"Invalid value. Enter the start time for the quiet hours, cancel, or disable. This should be in the format hh:mm:ss (e.g. 23:00:00)", true, this.GetType(), "setQuietHours", m.userFullName, -1, false, "", false, false, true);
                    }
                }
                return true;
            }
            else if (e.messageData == "ADDADMIN")
            {
                //try match against out presence list to get the userID
                List<chatPresence> members = c.getRecentChatUsers().Where(x => x.ToString() == m.text_msg).ToList();
                if (members.Count > 0)
                {
                    bool success = c.addAdmin(members[0].userID, m.userID);
                    TelegramAPI.SendMessage(m.chatID, success ? "Successfully added admin" : "Failed to add admin");
                }
                else
                {
                    TelegramAPI.SendMessage(m.chatID, "Failed to add admin");
                }
                return true;
            }
            else if (e.messageData == "REMOVEADMIN")
            {
                //try match against out presence list to get the userID
                long playerID = -1;
                bool success = long.TryParse(m.text_msg, out playerID);
                if (success) { success = c.removeAdmin(playerID, m.userID); }
                
                TelegramAPI.SendMessage(m.chatID, success ? "Successfully removed admin" : "Failed to remove admin");
                return true;
            }

            return false;
        }

        public static void getQuietTimes (long chatID, out TimeSpan startQuietHours, out TimeSpan endQuietHours )
        {
            chat c = Roboto.Settings.getChat(chatID);
            mod_standard_chatdata chatData = c.getPluginData<mod_standard_chatdata>();

            if (chatData == null)
            {
                //create the chat data. Get the plugin instance (we are in a static method). 
                RobotoModuleTemplate plugin = settings.getPlugin(typeof(mod_standard));
                plugin.initChatData(c);
                chatData = c.getPluginData<mod_standard_chatdata>();

            }

            startQuietHours = chatData.quietHoursStartTime;
            endQuietHours = chatData.quietHoursEndTime;
           
        }

        public static bool isTimeInQuietPeriod (long chatID, DateTime time )
        {
            TimeSpan start;
            TimeSpan end;
            getQuietTimes(chatID, out start, out end);


            //ignore the date for now - go off times. 
            TimeSpan currentTimePart = new TimeSpan(time.Hour, time.Minute, time.Second);

            //does the quiet period cross midnight? 
            if (start > end)
            {
                //looking for times after start or before end ?
                if (currentTimePart >= start || currentTimePart <= end)
                {
                    return true;
                }

            }
            //otherwise it's a normal period of time
            else
            {
                //looking for times after start AND before end.
                if (currentTimePart >= start && currentTimePart <= end)
                {
                    return true;
                }
            }

            return false;



        }
        
    }
}
