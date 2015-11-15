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
    /// Core Data to be stored in the XML store. NEeded to hold the "last update date" for backgorund processing
    /// </summary>
    [XmlType("mod_quote_core_data")]
    [Serializable]
    public class mod_quote_core_data : RobotoModuleDataTemplate
    {
       
        //internal mod_quote_data() { }
    }


    /// <summary>
    /// ChatData to be stored in the XML store
    /// </summary>
    [XmlType("mod_quote_data")]
    [Serializable]
    public class mod_quote_data : RobotoModuleChatDataTemplate
    {
        public List<mod_quote_quote> quotes = new List<mod_quote_quote>();
        public DateTime nextAutoQuoteAfter = DateTime.MinValue;
        public int autoQuoteHours = 24;
        public bool autoQuoteEnabled = true;
        //internal mod_quote_data() { }
    }

    /// <summary>
    /// Represents a single quote
    /// </summary>
    [XmlType("mod_quote_quote")]
    [Serializable]
    public class mod_quote_quote
    {
        public string by = "";
        public string text = "";
        public DateTime on = DateTime.Now;

        internal mod_quote_quote() { }
        public mod_quote_quote(String by, String text)
        {
            this.by = by;
            this.text = text;

        }
    }

    public class mod_quote : RobotoModuleTemplate
    {
        //TODO - Chat announcements? How do you know when someone is "back" though? 
        private mod_quote_core_data localData;

        public override void init()
        {
            pluginChatDataType = typeof(mod_quote_data);
            pluginDataType = typeof(mod_quote_core_data);

            chatHook = true;
            chatEvenIfAlreadyMatched = false;
            chatPriority = 5;
            this.backgroundHook = true;
            this.backgroundMins = 10;
            

        }

        public override string getMethodDescriptions()
        {
            return
                "quote_add - Adds a quote for the current chat" + "\n\r" +
                "quote - Picks a random quote from the chat's database" + "\n\r" +
                "quote_config - Configure how often to add quotes into chat";
        }

        public override void initData()
        {
            try
            {
                localData = Roboto.Settings.getPluginData<mod_quote_core_data>();
            }
            catch (InvalidDataException)
            {
                //Data doesnt exist, create, populate with sample data and register for saving
                localData = new mod_quote_core_data();
                sampleData();
                Roboto.Settings.registerData(localData);
            }
        }

        public override void initChatData(chat c)
        {
            mod_quote_data chatData = c.getPluginData<mod_quote_data>();

            if (chatData == null)
            {
                //Data doesnt exist, create, populate with sample data and register for saving
                chatData = new mod_quote_data();
                c.addChatData(chatData);
            }

        }

        private bool addQuote(string by, string text, chat c)
        {
            mod_quote_data localData = c.getPluginData<mod_quote_data>();

            if (!quoteExists(by, text, c))
            {
                localData.quotes.Add(new mod_quote_quote(by, text));
                Roboto.Settings.save();
                return true;
            }
            return false;
        }

        private bool quoteExists(string by, string text, chat c)
        {
            mod_quote_data localData = c.getPluginData<mod_quote_data>();
            foreach (mod_quote_quote q in localData.quotes)
            {
                if (q.by == by && q.text == text)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// gets a FORMATTED string for the quote
        /// </summary>
        /// <returns></returns>
        private string getQuote(chat c)
        {
            mod_quote_data localData = c.getPluginData<mod_quote_data>();
            if (localData.quotes.Count > 0)
            {

                mod_quote_quote q = localData.quotes[settings.getRandom(localData.quotes.Count)];
                return (
                    "*" + q.by + "* said \r\n" +
                    q.text + "\r\n" +
                    "on " + q.on.ToString("g"));
            }
            else
            {
                return "No quotes in DB";
            }
        }


        public override bool chatEvent(message m, chat c = null)
        {
            mod_quote_data chatData = (mod_quote_data)c.getPluginData(typeof(mod_quote_data));
            bool processed = false;
            if (c != null)
            {
                if (m.text_msg.StartsWith("/quote_add"))
                {
                    TelegramAPI.GetExpectedReply(c.chatID, m.userID, "Who is the quote by", false, typeof(mod_quote), "WHO", m.message_id, true);
                    processed = true;
                }
                else if (m.text_msg.StartsWith("/quote_config"))
                {
                    List<string> options = new List<string>();
                    options.Add("Set Duration");
                    options.Add("Toggle automatic quotes");
                    string keyboard = TelegramAPI.createKeyboard(options, 1);
                    TelegramAPI.GetExpectedReply(c.chatID, m.userID,
                        "Quotes are currently " + (chatData.autoQuoteEnabled == true? "enabled" : "disabled") 
                        + " and set to announce every " + chatData.autoQuoteHours.ToString() + " hours"
                        , false, typeof(mod_quote), "CONFIG", m.message_id, true, keyboard);
                }
                else if (m.text_msg.StartsWith("/quote"))
                {
                    TelegramAPI.SendMessage(m.chatID, getQuote(c), true, m.message_id);
                    processed = true;
                }

            }
            return processed;
        }

        protected override void backgroundProcessing()
        {
            foreach (chat c in Roboto.Settings.chatData)
            {
                mod_quote_data localData = c.getPluginData<mod_quote_data>();
                
                if (localData.autoQuoteEnabled && DateTime.Now > localData.nextAutoQuoteAfter && localData.quotes.Count > 0)
                {
                    TelegramAPI.SendMessage(c.chatID, getQuote(c));
                    int maxMins = localData.autoQuoteHours * 60;
                    //go back 1/8, then add rand 1/4 on
                    int randomMins = settings.getRandom((localData.autoQuoteHours * 60) /4);
                    maxMins = maxMins - ( maxMins / 8) + randomMins;
                    localData.nextAutoQuoteAfter = DateTime.Now.AddMinutes(maxMins);
                    
                }

            }
        }

        public override void sampleData()
        {
          
        }

        public override bool replyReceived(ExpectedReply e, message m)
        {
            chat c = Roboto.Settings.getChat(e.chatID);
            mod_quote_data chatData = (mod_quote_data)c.getPluginData(typeof(mod_quote_data));

            //Adding quotes
            if (e.messageData == "WHO")
            {
                TelegramAPI.GetExpectedReply(e.chatID, m.userID, "What was the quote from " + m.text_msg, false, typeof(mod_quote), "TEXT " + m.text_msg, m.message_id, true);
                return true;
            }

            else if (e.messageData.StartsWith("TEXT"))
            {
                string quoteBy = e.messageData.TrimStart("TEXT ".ToCharArray());
                bool success = addQuote(quoteBy, m.text_msg, c);
                TelegramAPI.SendMessage(m.chatID, "Added " + m.text_msg + " by " + quoteBy + " " + (success ? "successfully" : "but fell on my ass"));
                return true;
            }

            //CONFIG
            else if (e.messageData.StartsWith("CONFIG"))
            {
                if (m.text_msg == "Set Duration")
                {
                    TelegramAPI.GetExpectedReply(e.chatID, m.userID, "How long between updates?" + m.text_msg, false, typeof(mod_quote), "DURATION" + m.text_msg, m.message_id, true);
                    return true;
                }
                else if (m.text_msg == "Toggle automatic quotes")
                {
                    chatData.autoQuoteEnabled = !chatData.autoQuoteEnabled;
                    TelegramAPI.SendMessage(c.chatID, "Quotes are now " + (chatData.autoQuoteEnabled == true ? "enabled" : "disabled"), false, -1, true);
                    return true;
                }
            }

            //DURATION
            else if (e.messageData.StartsWith("DURATION"))
            {
                int hours = -1;
                if (int.TryParse(m.text_msg, out hours) && hours >= -1)
                {
                    chatData.autoQuoteHours = hours;
                    TelegramAPI.SendMessage(c.chatID, "Quote schedule set to every " + hours.ToString() + " hours.", false, -1, true);
                }
                else if (m.text_msg != "Cancel")
                {
                    TelegramAPI.GetExpectedReply(e.chatID, m.userID, "Not a number. How many hours between updates, or 'Cancel' to cancel" + m.text_msg, false, typeof(mod_quote), "DURATION" + m.text_msg, m.message_id, true);
                }
                return true;

            }
            return false;
        }
    }
}
