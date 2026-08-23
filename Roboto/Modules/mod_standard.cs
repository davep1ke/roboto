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
        //Throttles the logs-table purge the same way lastSaveToDiskDateTime throttles the settings
        //save - once a day is plenty for a 30-day retention window, and avoids a DELETE query on
        //every single background pass once the real scheduler (a much shorter interval) exists.
        public DateTime lastLogPurgeDateTime = DateTime.MinValue;
    }

    [XmlType("mod_standard_chatdata")]
    [Serializable]
    public class mod_standard_chatdata : RobotoModuleChatDataTemplate
    {
        //Timespan won't serialise, so need to use a backing "long" to store the actual value.
        public long x_quietHoursStartTime = TimeSpan.MinValue.Ticks;
        public long x_quietHoursEndTime = TimeSpan.MinValue.Ticks;

        /// <summary>TelegramAPI.DeAdminSelf's basic-group explanation is throttled to once a week,
        /// not sent on every check - PromoteChatMember can never actually succeed for a basic
        /// (non-super) group, so EnsureNotAdminInAnyChat's background sweep would otherwise re-send
        /// the exact same message every 5 minutes forever, as long as the bot stays admin there
        /// (confirmed live - found by the user asking "does this fire repeatedly, or just once").
        /// A gentle periodic reminder rather than either a one-time warning (easy to miss/forget)
        /// or a constant nag - user's explicit call. DateTime.MinValue (never warned) always
        /// triggers immediately, same as this project's other lastXDateTime throttles.</summary>
        public DateTime lastBasicGroupAdminWarningDateTime = DateTime.MinValue;

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

        public override void startupChecks()
        {
            Roboto.Settings.stats.registerStatType("Expected Replies", this.GetType(), System.Drawing.Color.LawnGreen, stats.displaymode.line, stats.statmode.absolute);
        }


        public override string getMethodDescriptions()
        {
            return
                "help - Returns this list of commands" + "\r\n" +
                "start - Starts listening to the chat" + "\r\n" +
                "stop - Stops listening to the chat, until a START is entered." + "\r\n" +
                "save - Saves any outstanding in memory stuff to disk." + "\r\n" +
                "stats - Returns an overview of the currently loaded plugins." + "\r\n" +
                "version - Returns the git commit and build date this instance is running." + "\r\n" +
                "setquiethours - Sets quiet hours for the chat." + "\r\n" +
                "addadmin - adds an chat administrator" + "\r\n" +
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
            foreach (RobotoModuleTemplate plugin in Plugins.plugins)
            {
                methods += "\r\n" + plugin.getMethodDescriptions(); 
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
            if (((mod_standard_data)localData).lastSaveToDiskDateTime.AddMinutes(Roboto.Settings.saveXMLeveryXMins) < DateTime.Now)
            {
                ((mod_standard_data)localData).lastSaveToDiskDateTime = DateTime.Now;
                Roboto.Settings.save();
            }

            //do general housekeeping
            Roboto.Settings.stats.houseKeeping();
            Presence.backgroundProcessing();
            Messaging.backgroundProcessing();
            // Safety-net for bot self-de-admin (TelegramAPI's MyChatMember reactive handler,
            // phase 9) - added alongside it, not instead, per explicit request.
            TelegramAPI.EnsureNotAdminInAnyChat();
            Chats.removeDormantChats();

            //purge the logs table of anything older than 30 days - once a day is plenty (see
            //lastLogPurgeDateTime's own comment).
            if (((mod_standard_data)localData).lastLogPurgeDateTime.AddDays(1) < DateTime.Now)
            {
                ((mod_standard_data)localData).lastLogPurgeDateTime = DateTime.Now;
                int purged = Roboto.Store.PurgeLogsOlderThan(DateTime.UtcNow.AddDays(-30));
                if (purged > 0) { Roboto.log.log("Purged " + purged + " log rows older than 30 days", logging.loglevel.low); }
            }

        }

        public override bool chatEvent(message m, chat c = null)
        {
            bool processed = false;

            if (m.text_msg.StartsWith("/help") && c != null && c.muted == false)
            {

                mod_standard_chatdata chatData = c.getPluginData<mod_standard_chatdata>();
                string openingMessage = "This is chat " + (c.chatTitle == null ? "" : c.chatTitle) + " (" + c.chatID + "). " +"\r\n";
                if (chatData.quietHoursStartTime != TimeSpan.MinValue && chatData.quietHoursEndTime != TimeSpan.MinValue)
                {
                    openingMessage += "Quiet time set between " + chatData.quietHoursStartTime.ToString("c") + " and " + chatData.quietHoursEndTime.ToString("c") + ". \r\n";
                }

                Messaging.SendMessage(m.chatID, openingMessage +  getAllMethodDescriptions());
                processed = true;
            }
            else if (m.text_msg.StartsWith("/save"))
            {
                Roboto.Settings.save();
                Messaging.SendMessage(m.chatID, "Saved settings");
                processed = true;
            }
            else if (m.text_msg.StartsWith("/stop") && c != null)
            {
                c.muted = true;
                Messaging.SendMessage(m.chatID, "I am now ignoring all messages in this chat until I get a /start command. ");
                //TODO - make sure we abandon any games

                processed = true;
            }
            else if (m.text_msg.StartsWith("/start") && c != null && c.muted == true)
            {
                c.muted = false;
                Messaging.SendMessage(m.chatID, "I am listening for messages again. Type /help for a list of commands." + "\r\n" + getAllWelcomeDescriptions());
                processed = true;
            }
            else if (m.text_msg.StartsWith("/start"))
            {
                //a default /start message where we arent on pause. Might be in group or private chat. 
                Messaging.SendMessage(m.chatID, getAllWelcomeDescriptions());

            }


            else if (m.text_msg.StartsWith("/background"))
            {
                //kick off the background loop. 
                Plugins.backgroundProcessing(true);
            }

            else if (m.text_msg.StartsWith("/setquiethours") && c != null)
            {
                Messaging.SendQuestion(m.chatID, m.userID, "Enter the start time for the quiet hours, cancel, or disable. This should be in the format hh:mm:ss (e.g. 23:00:00)", true, this.GetType(), "setQuietHours");
                processed = true;
            }

            else if (m.text_msg.StartsWith("/stats"))
            {
                TimeSpan uptime = DateTime.Now.Subtract(Roboto.startTime);

                String statstxt = "I is *@" + Roboto.Settings.botUserName + "*" + "\r\n" +
                    "Uptime: " + uptime.Days.ToString() + " days, " + uptime.Hours.ToString() + " hours and " + uptime.Minutes.ToString() + " minutes." + "\r\n" +
                    "I currently know about " + Roboto.Settings.chatData.Count().ToString() + " chats." + "\r\n" +
                    "The following plugins are currently loaded:" + "\r\n";

                foreach (RobotoModuleTemplate plugin in Plugins.plugins)
                {
                    statstxt += "*" + plugin.GetType().ToString() + "*" + "\r\n";
                    statstxt += plugin.getStats() + "\r\n";
                }

                Messaging.SendMessage(m.chatID, statstxt, m.userFullName, true);
                processed = true;
            }
            else if (m.text_msg.StartsWith("/version"))
            {
                // Answers "which build is actually running" - real deployments are traced back to
                // an exact git commit (Roboto.csproj embeds it at compile time via AssemblyMetadata,
                // baked into the assembly itself so it survives however the container gets
                // launched), not a separately-maintained version number - there's no versioning
                // scheme in this project beyond the commit history itself.
                Messaging.SendMessage(m.chatID,
                    "Git commit: " + RobotoChatBot.BuildInfo.GitCommit + "\r\n" +
                    "Built: " + RobotoChatBot.BuildInfo.BuildDate,
                    m.userFullName, true);
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
                            Messaging.SendMessage(m.chatID, "Added " + m.userFullName + " as admin.");
                        }
                        else
                        {
                            Messaging.SendMessage(m.chatID, "Something went wrong! ");
                            log("Error adding user as an admin", logging.loglevel.high);
                        }
                    }
                    else
                    {
                        //create a keyboard with the recent chat members
                        List<string> members = new List<string>();
                        foreach (chatPresence p in c.getRecentChatUsers()) { members.Add(p.ToString()); }
                        //send keyboard to player requesting admin. 
                        Messaging.SendQuestion(m.chatID, m.userID, "Who do you want to add as admin?", true, typeof(mod_standard), "ADDADMIN", m.userFullName, -1, false, TelegramAPI.createKeyboard(members, 2));
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
                        Messaging.SendMessage(m.chatID, "Group currently doesnt have any admins!");
                    }
                    else
                    {
                        //create a keyboard with the recent chat members
                        List<string> members = new List<string>();
                        foreach (long userID in c.chatAdmins) { members.Add(userID.ToString()); }
                        //send keyboard to player requesting admin. 
                        Messaging.SendQuestion(m.chatID, m.userID, "Who do you want to remove as admin?", true, typeof(mod_standard), "REMOVEADMIN", m.userFullName, -1, false, TelegramAPI.createKeyboard(members, 2));
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
                    // .png, not the legacy .jpg - stats.generateImage renders via ScottPlot, which
                    // encodes PNG (phase 6), not the old WinForms JPEG output this filename used to match.
                    Messaging.SendPhoto(m.chatID, "Stats", image, "StatsGraph.png", "application/octet-stream", m.message_id, false);
                }
                else
                {
                    Messaging.SendMessage(m.chatID, "No statistics were found that matched your input, sorry!");
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
                foreach (RobotoModuleTemplate plugin in Plugins.plugins)
                {
                    string moduleDesc = plugin.getWelcomeDescriptions();
                    if (moduleDesc != null) { description += "\r\n" + moduleDesc; }
                }
                return description;
            }
        }
        public override bool replyReceived(ExpectedReply e, message m, bool messageFailed = false)
        {
            chat c = Chats.getChat(e.chatID);
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
                    Messaging.SendMessage(e.chatID, "Quiet hours have been disabled");
                }
                else
                {
                    //try parse it 
                    TimeSpan s;
                    bool success = TimeSpan.TryParse(m.text_msg, out s);
                    if (success && s > TimeSpan.Zero && s.TotalDays < 1)
                    {
                        chatData.quietHoursStartTime = s;
                        Messaging.SendQuestion(e.chatID, m.userID, "Enter the wake time for the quiet hours, cancel, or disable. This should be in the format hh:mm:ss (e.g. 23:00:00)", true, this.GetType(), "setWakeHours", m.userFullName, -1, false, null, false, false, true);
                    }
                    else
                    {
                        Messaging.SendQuestion(e.chatID, m.userID,  "Invalid value. Enter the start time for the quiet hours, cancel, or disable. This should be in the format hh:mm:ss (e.g. 23:00:00)", true, this.GetType(), "setQuietHours", m.userFullName, -1, false, null, false, false, true);
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
                    Messaging.SendMessage(e.chatID, "Quiet hours have been disabled");
                }
                else
                {
                    //try parse it 
                    TimeSpan s;
                    bool success = TimeSpan.TryParse(m.text_msg, out s);
                    if (success && s > TimeSpan.Zero && s.TotalDays < 1)
                    {
                        chatData.quietHoursEndTime = s;
                        Messaging.SendMessage(e.chatID, "Quiet time set from " + chatData.quietHoursStartTime.ToString("c") + " to " + chatData.quietHoursEndTime.ToString("c"));   
                    }
                    else
                    {
                        Messaging.SendQuestion(e.chatID, m.userID,"Invalid value. Enter the start time for the quiet hours, cancel, or disable. This should be in the format hh:mm:ss (e.g. 23:00:00)", true, this.GetType(), "setQuietHours", m.userFullName, -1, false, null, false, false, true);
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
                    Messaging.SendMessage(m.chatID, success ? "Successfully added admin" : "Failed to add admin");
                }
                else
                {
                    Messaging.SendMessage(m.chatID, "Failed to add admin");
                }
                return true;
            }
            else if (e.messageData == "REMOVEADMIN")
            {
                //try match against out presence list to get the userID
                long playerID = -1;
                bool success = long.TryParse(m.text_msg, out playerID);
                if (success) { success = c.removeAdmin(playerID, m.userID); }
                
                Messaging.SendMessage(m.chatID, success ? "Successfully removed admin" : "Failed to remove admin");
                return true;
            }

            return false;
        }

        public static void getQuietTimes (long chatID, out TimeSpan startQuietHours, out TimeSpan endQuietHours )
        {
            chat c = Chats.getChat(chatID);
            mod_standard_chatdata chatData = c.getPluginData<mod_standard_chatdata>();

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
