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
    /// General Data to be stored in the plugin XML store.
    /// </summary>
    [XmlType("mod_birthday_coredata")]
    [Serializable]
    public class mod_birthday_coredata : RobotoModuleDataTemplate
    {
        public DateTime lastDayProcessed = DateTime.MinValue;
        //internal mod_birthday_coredata() { }
    }

    /// <summary>
    /// CHAT Data to be stored in the XML store
    /// </summary>
    [XmlType("mod_birthday_data")]
    [Serializable]
    public class mod_birthday_data : RobotoModuleChatDataTemplate
    {
        public List<mod_birthday_birthday> birthdays = new List<mod_birthday_birthday>();
        public DateTime lastDayProcessed = DateTime.MinValue;

        public void listBirthdays()
        {
            String message = "I know about the following birthdays!";
            foreach (mod_birthday_birthday b in birthdays.OrderBy(x => x.birthday.Subtract(new DateTime(x.birthday.Year,1,1))))
            {
                message += b.birthday.ToString("\n\r" + "yyyy-MM-dd") + "\t\t - *" + b.name + "*";
            }
            TelegramAPI.SendMessage(chatID, message, null, true);
        }

        /// <summary>
        /// Never purge chats with birthday data
        /// </summary>
        /// <returns></returns>
        public override bool isPurgable()
        {
            return false;
        }
    }
    

    /// <summary>
    /// Represents a birthday
    /// </summary>
    public class mod_birthday_birthday
    {
        public String name;
        public DateTime birthday;

        internal mod_birthday_birthday() { }
        public mod_birthday_birthday (String name, DateTime birthday)
        {
            this.name = name;
            this.birthday = birthday;
        }

    }

    
    public class mod_birthday : RobotoModuleTemplate
    {
        private mod_birthday_coredata localData;

        public override void init()
        {
            pluginDataType = typeof(mod_birthday_coredata);
            pluginChatDataType = typeof(mod_birthday_data);

            chatHook = true;
            chatEvenIfAlreadyMatched = false;
            chatPriority = 5;

            backgroundHook = true;
            backgroundMins = 120;

        }

        public override string getMethodDescriptions()
        {
            return
                "birthday_add - Adds a birthday reminder for the current chat" + "\n\r" +
                "birthday_remove - Removes a birthday reminder for the current chat" + "\n\r" +
                "birthday_list - Shows the list of birthdays that have been added";
        }

        public override string getWelcomeDescriptions()
        {
            return "Birthday reminder plugin - type /birthday_add to add someones birthday!";

        }

        public override void initData()
        {
            try
            {
                localData = Roboto.Settings.getPluginData<mod_birthday_coredata>();
            }
            catch (InvalidDataException)
            {
                //Data doesnt exist, create, populate with sample data and register for saving
                localData = new mod_birthday_coredata();
                sampleData();
                Roboto.Settings.registerData(localData);
            }

        }

        public override void initChatData(chat c)
        {
            mod_birthday_data chatData = c.getPluginData<mod_birthday_data>();
           
            if (chatData == null)
            {
                //Data doesnt exist, create, populate with sample data and register for saving
                chatData = new mod_birthday_data();
                c.addChatData(chatData);
            }
        }


        public override bool chatEvent(message m, chat c = null)
        {
            bool processed = false;
            if (c != null) //Needs to be done in a chat!
            {
                mod_birthday_data chatData = c.getPluginData< mod_birthday_data>();

                if (m.text_msg.StartsWith("/birthday_add"))
                {
                    TelegramAPI.GetExpectedReply(m.chatID, m.userID,  "Whose birthday do you want to add?", true, this.GetType(), "ADD");
                    processed = true;
                }
                else if (m.text_msg.StartsWith("/birthday_remove"))
                {
                    TelegramAPI.GetExpectedReply(m.chatID, m.userID, "Whose birthday do you want to remove?", true, this.GetType(), "REMOVE");
                    processed = true;
                }

                else if (m.text_msg.StartsWith("/birthday_list"))
                {
                    chatData.listBirthdays();
                    processed = true;
                }

            }
            return processed;
        }

        public override bool replyReceived(ExpectedReply e, message m, bool messageFailed = false)
        {
            chat c = Roboto.Settings.getChat(e.chatID);
            mod_birthday_data chatData = c.getPluginData<mod_birthday_data>();

            if (e.messageData == "ADD")
            {
                //reply to add word
                TelegramAPI.GetExpectedReply(e.chatID, m.userID, "What is their Birthday? (DD-MON-YYYY format, e.g. 01-JAN-1900)", true, this.GetType(), "ADD2-" + m.text_msg);
                return true;
            }

            else if (e.messageData.StartsWith("ADD2"))
            {
                string uname = e.messageData.Substring(5);
                DateTime birthday;
                bool success = DateTime.TryParse(m.text_msg, out birthday);
                if (success)
                {
                    mod_birthday_birthday data = new mod_birthday_birthday(uname, birthday);
                    addBirthday(data, c );
                    TelegramAPI.SendMessage(e.chatID, "Added " + uname + "'s Birthday (" + birthday.ToString("yyyy-MM-dd") + ")");
                }
                else
                {
                    Console.WriteLine("Failed to add birthday");
                    TelegramAPI.SendMessage(m.chatID, "Failed to add birthday");
                }
                return true;
            }

            else if (e.messageData == "REMOVE")
            {

                bool success = removeBirthday(m.text_msg, c);
                TelegramAPI.SendMessage(m.chatID, "Removed birthday for " + m.text_msg + " " + (success ? "successfully" : "but fell on my ass"));
                return true;
            }
            return false;
        }

        

        protected override void backgroundProcessing()
        {
            foreach (chat c in Roboto.Settings.chatData)
            {
                mod_birthday_data localData = c.getPluginData<mod_birthday_data>();

                //have we already processed today? 
                if (localData.lastDayProcessed.Month != DateTime.Now.Month || localData.lastDayProcessed.Day != DateTime.Now.Day)
                {
                    localData.lastDayProcessed = DateTime.Now;
                    foreach (mod_birthday_birthday b in localData.birthdays)
                    {
                        if (b.birthday.Day == DateTime.Now.Day && b.birthday.Month == DateTime.Now.Month)
                        {
                            TelegramAPI.SendMessage(c.chatID, "Happy Birthday to " + b.name + "!");
                        }
                    }

                }
            }
        }

        
        public void addBirthday(mod_birthday_birthday birthday, chat c)
        {
            mod_birthday_data localData = c.getPluginData<mod_birthday_data>();
            localData.birthdays.Add(birthday);
        }

        public bool removeBirthday(string name, chat c)
        {
            mod_birthday_data localData = c.getPluginData<mod_birthday_data>();
            mod_birthday_birthday birthday = null;
            foreach (mod_birthday_birthday item in localData.birthdays)
            {
                if (item.name == name) { birthday = item; }
            }
            if (birthday != null)
            {
                return localData.birthdays.Remove(birthday);
            }
            return false;
        }

        

    }
}
