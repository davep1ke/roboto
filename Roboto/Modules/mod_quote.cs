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
    [XmlType("mod_quote_data")]
    [Serializable]
    public class mod_quote_data : RobotoModuleChatDataTemplate
    {
        public List<mod_quote_quote> quotes = new List<mod_quote_quote>();
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

        public override void init()
        {
            pluginDataType = typeof(mod_quote_data);

            chatHook = true;
            chatEvenIfAlreadyMatched = false;
            chatPriority = 5;

        }

        public override string getMethodDescriptions()
        {
            return
                "addquote - Adds a quote for the current chat" + "\n\r" +
                "quote - Picks a randon quote from the chat's database";
        }

        public override void initData()
        {

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
            bool processed = false;
            if (c != null)
            {
                if (m.text_msg.StartsWith("/addquote"))
                {
                    TelegramAPI.GetReply(m.chatID, "Who is the quote by", m.message_id, true);
                    processed = true;
                }
                else if (m.isReply && m.replyOrigMessage == "Who is the quote by" && m.replyOrigUser == Roboto.Settings.botUserName)
                {
                    TelegramAPI.GetReply(m.chatID, "What was the quote from " + m.text_msg, m.message_id, true);
                    processed = true;
                }
                else if (m.isReply && m.replyOrigMessage.StartsWith("What was the quote from ") && m.replyOrigUser == Roboto.Settings.botUserName)
                {
                    string quoteBy = m.replyOrigMessage.Replace("What was the quote from ", "");
                    bool success = addQuote(quoteBy, m.text_msg,c);
                    TelegramAPI.SendMessage(m.chatID, "Added " + m.text_msg + " by " + quoteBy + " " + (success ? "successfully" : "but fell on my ass"));
                    processed = true;
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
            throw new NotImplementedException();
        }

        public override void sampleData()
        {
          
        }


    }
}
