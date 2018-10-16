using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RobotoChatBot
{
    /// <summary>
    /// represents a group chat and all the data stored abotu it. Doesnt exist for PERSON chats, just group
    /// </summary>
    public class chat
    {
        public long chatID;
        //public bool enabled = false; //assume chats are disabled until we get a /start - deprecated. Uses "muted"
        public DateTime lastupdate = DateTime.Now; //track when our last message was. Discard idle chats.
        public string chatTitle = "";
        public List<Modules.RobotoModuleChatDataTemplate> chatData = new List<Modules.RobotoModuleChatDataTemplate>();
        public List<long> chatAdmins = new List<long>();
        public bool muted = false;

        internal chat() { }

        public chat(long chatID, string chatTitle)
        {
            this.chatID = chatID;
            this.chatTitle = chatTitle;
            initPlugins();
        }

        /// <summary>
        /// Loop through the plugins and get them to populate stub data for this chat, if they need to
        /// </summary>
        public void initPlugins()
        {
            foreach (Modules.RobotoModuleTemplate plugin in settings.plugins)
            {
                plugin.initChatData(this);
                plugin.validateChatData(this);
                //find and validate the data

            }

        }

        public void resetLastUpdateTime()
        {
            lastupdate = DateTime.Now;
        }

        /*deprecated public void enable()
        {
            enabled = true;
        }
        public void disable()
        {
            enabled = false;
        }*/

        public void addChatData(Modules.RobotoModuleChatDataTemplate data)
        {
            data.chatID = this.chatID;
            //replace current Chat data, or add if doesnt exist
            Modules.RobotoModuleChatDataTemplate found = null;
            foreach (Modules.RobotoModuleChatDataTemplate current in chatData)
            {
                if (current.GetType() == data.GetType())
                {
                    found = current;
                }
            }
            if (found != null)
            {
                Roboto.log.log("Chat data Already exists!");
                throw new InvalidOperationException("Chat data Already exists!");
            }

            chatData.Add(data);
        }

        public T getPluginData<T>()
        {
            foreach (Modules.RobotoModuleChatDataTemplate existing in chatData)
            {
                if (existing.GetType() == typeof(T))
                {
                    //Console.WriteLine("Plugin data of type " + data.GetType().ToString() + " already exists!");
                    T retVal = (T)Convert.ChangeType(existing, typeof(T));
                    return retVal;
                }
            }

            Roboto.log.log("Couldnt find plugin data of type " + typeof(T).ToString());
            return default(T);
        }


        internal Modules.RobotoModuleChatDataTemplate getPluginData(Type t, bool supressWarning = false)
        {
            foreach (Modules.RobotoModuleChatDataTemplate existing in chatData)
            {
                if (existing.GetType() == t)
                {
                    return existing;
                }
            }
            if (!supressWarning)
            {
                Roboto.log.log("Couldnt find plugin data of type " + t.ToString());
            }
            return null;
        }

        public bool isPurgable()
        {
            //If no plugin data is being kept, remaining chat summary data can be purged. Otherwise keep
            if (chatData.Count == 0) { return true; }
            return false;
        }

        public void tryPurgeData()
        {
            List<Modules.RobotoModuleChatDataTemplate> dataToPurge = chatData.Where(cd => cd.isPurgable()).ToList();

            foreach (Modules.RobotoModuleChatDataTemplate d in dataToPurge)
            {
                Roboto.log.log("About to remove " + d.GetType() + " data for chat " + chatID, logging.loglevel.high);
                chatData.Remove(d);
            }


                
        }

        public void setTitle(string chatTitle)
        {
            if (this.chatTitle != chatTitle)
            {
                Roboto.log.log("Chat title changed from '" + this.chatTitle + "' to '" + chatTitle + "'", logging.loglevel.warn);
                this.chatTitle = chatTitle;
            }
        }

        public override string ToString()
        {
            string text = "";
            if (chatTitle != null) { text = chatTitle; }
            return text + "(" + chatID + ")";
        }

        public bool addAdmin(long userID, long callerID)
        {
            if (isChatAdmin(callerID))
            {
                if (!chatAdmins.Contains(userID)) { chatAdmins.Add(userID); }
                return true;
            }
            else
            {
                return false;
            }
        }

        public bool isChatAdmin(long userID)
        {
            if (!chatHasAdmins() || chatAdmins.Contains(userID)) { return true; } else { return false;}
        }

        public bool removeAdmin(long userID, long callerID)
        {
            if (isChatAdmin(callerID))
            {
                return chatAdmins.Remove(userID);
                 }
            else
            {
                return false;
            }
        }
        public bool chatHasAdmins()
        {
            if (chatAdmins.Count() > 0) { return true; } else { return false; }
        }

        public List<chatPresence> getRecentChatUsers()
        {
            return Roboto.Settings.getChatRecentMembers(this.chatID);
        }

        /// <summary>
        /// Check if a player has admin privs. Send a message to the group chat if the user isnt an admin. 
        /// </summary>
        /// <param name="userID"></param>
        /// <returns></returns>
        public bool checkAdminPrivs(long userID, long sendFailMessageTo)
        {
            Roboto.log.log("Checking admin privs for " + userID + " in " + chatID);
            if (isChatAdmin(userID)) { return true; }
            else
            {
                //send fail message
                TelegramAPI.SendMessage(sendFailMessageTo, @"https://www.youtube.com/watch?v=YEwlW5sHQ4Q");
                return false;
            }
        }
    }
}
