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
    public class mod_quote_data : RobotoModuleDataTemplate
    {
        public List<mod_quote_quote> quotes = new List<mod_quote_quote>();
        internal mod_quote_data() { }
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
        private mod_quote_data localData;

        public override void init()
        {
            pluginDataType = typeof(mod_quote_data);

            chatHook = true;
            chatEvenIfAlreadyMatched = false;
            chatPriority = 5;

        }

        public override void initData()
        {
            try
            {
                localData = Roboto.Settings.getPluginData<mod_quote_data>();
            }
            catch (InvalidDataException)
            {
                localData = new mod_quote_data();
                sampleData();
                Roboto.Settings.registerData(localData);
            }

        }

        public override void initChatData()
        {
            
        }

        private bool addQuote(string by, string text)
        {

            if (!quoteExists(by, text))
            {


                localData.quotes.Add(new mod_quote_quote(by, text));
                Roboto.Settings.save();
                return true;
            }
            return false;

        }

        private bool quoteExists(string by, string text)
        {
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
        private string getQuote()
        {
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


        public override bool chatEvent(message m)
        {
            bool processed = false;

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
                bool success = addQuote(quoteBy, m.text_msg);
                TelegramAPI.SendMessage(m.chatID, "Added " + m.text_msg + " by " + quoteBy + " " + (success ? "successfully" : "but fell on my ass"));
                processed = true;
            }
            else if (m.text_msg.StartsWith("/quote"))
            {
                TelegramAPI.SendMessage(m.chatID, getQuote(), true, m.message_id);
                processed = true;
            }
            
            return processed;
        }

        protected override void backgroundProcessing()
        {
            throw new NotImplementedException();
        }


        public override void sampleData()
        {
            localData.quotes.Add(new mod_quote_quote("Michael Bluth", "I've made a huge mistake"));
        }

    }
}
