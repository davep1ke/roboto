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
    /// Data to be stored in the XML store
    /// </summary>
    [XmlType("mod_birthday_data")]
    [Serializable]
    public class mod_birthday_data : RobotoModuleDataTemplate
    {
        public List<mod_birthday_birthday> birthdays = new List<mod_birthday_birthday>();
        public DateTime lastDayProcessed = DateTime.MinValue;
        internal mod_birthday_data() { }
    }
    //TODO - this should be stored for a specific chat, not in general. 
    [XmlType("mod_birthday_birthday")]
    [Serializable]
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
        private mod_birthday_data localData;

        public override void init()
        {
            pluginDataType = typeof(mod_birthday_data);

            chatHook = true;
            chatEvenIfAlreadyMatched = false;
            chatPriority = 5;

            backgroundHook = true;
            backgroundMins = 30;

        }

        public override void initData()
        {
            try
            {
                localData = Roboto.Settings.getPluginData<mod_birthday_data>();
            }
            catch (InvalidDataException)
            {
                localData = new mod_birthday_data();
                sampleData();
                Roboto.Settings.registerData(localData);
            }

        }

        public override void initChatData()
        {
            
        }

       

        public override bool chatEvent(message m)
        {
            bool processed = false;

            if (m.text_msg.StartsWith("/birthday_add"))
            {
                TelegramAPI.GetReply(m.chatID, "Whose birthday do you want to add?", m.message_id, true);
                processed = true;
            }
            else if (m.text_msg.StartsWith("/birthday_remove"))
            {
                TelegramAPI.GetReply(m.chatID, "Whose birthday do you want to remove?", m.message_id, true);
                processed = true;
            }
            else if (m.isReply && m.replyOrigMessage == "Whose birthday do you want to add?" && m.replyOrigUser == Roboto.Settings.botUserName)
            {
                //reply to add word
                TelegramAPI.GetReply(m.chatID, "What birthday does " + m.text_msg + " have? (DD-MON-YYYY format, e.g. 01-JAN-1900)", m.message_id, true);
                processed = true;
            }
            
            else if (m.isReply && m.replyOrigMessage.StartsWith( "What birthday does ") && m.replyOrigUser == Roboto.Settings.botUserName)
            {
                string uname = m.replyOrigMessage.Substring(19);
                uname = m.replyOrigMessage.Substring(0, m.replyOrigMessage.IndexOf(" have?"));
                DateTime birthday;
                bool success = DateTime.TryParse(m.text_msg, out birthday);
                if (success)
                {
                    mod_birthday_birthday data = new mod_birthday_birthday(uname, birthday);
                    addBirthday(data);
                }
                else
                {
                    Console.WriteLine("Failed to add birthday");
                    TelegramAPI.SendMessage(m.chatID, "Failed to add birthday");
                }
                processed = true;
            }

            else if (m.isReply && m.replyOrigMessage == "Whose birthday do you want to remove?" && m.replyOrigUser == Roboto.Settings.botUserName)
            {

                bool success = removeBirthday(m.text_msg);
                TelegramAPI.SendMessage(m.chatID, "Removed birthday for " + m.text_msg + " " + (success ? "successfully" : "but fell on my ass"));
                processed = true;
            }
            
            return processed;
        }

        protected override void backgroundProcessing()
        {
            //have we already processed today? 
            if (localData.lastDayProcessed.Month != DateTime.Now.Month || localData.lastDayProcessed.Day != DateTime.Now.Day)
            {
                localData.lastDayProcessed = DateTime.Now;
                foreach (mod_birthday_birthday b in localData.birthdays)
                {
                    if (b.birthday.Day == DateTime.Now.Day && b.birthday.Month == DateTime.Now.Month)
                    {
                        //TODO - CHAT ID
                        TelegramAPI.SendMessage(120498152, "Happy Birthday to " + b.name);
                    }

                }

            }

        }


        public override void sampleData()
        {
            localData.birthdays.Add(new mod_birthday_birthday("Jesus", DateTime.MinValue));

        }


        public void addBirthday(mod_birthday_birthday birthday)
        {
            localData.birthdays.Add(birthday);
        }

        public bool removeBirthday(string name)
        {
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
