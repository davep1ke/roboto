﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace RobotoChatBot.Modules
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
        //new version, using multi-quotes
        public List<mod_quote_multiquote> multiquotes = new List<mod_quote_multiquote>();
        public DateTime nextAutoQuoteAfter = DateTime.MinValue;
        public int autoQuoteHours = 24;
        public bool autoQuoteEnabled = true;
        //internal mod_quote_data() { }
        
        /// <summary>
        /// Never purge chats with quote data
        /// </summary>
        /// <returns></returns>
        public override bool isPurgable()
        {
            if (multiquotes.Count > 0) { return false; }
            return true;
        }
    }

    /// <summary>
    /// Represents a single quote. OBSOLETE - USE mod_quote_multiquote
    /// </summary>
    [XmlType("mod_quote_quote")]
    [Serializable]
    public class mod_quote_quote
    {
        public string by = "";
        public string text = "";
        public DateTime on = DateTime.Now;

        internal mod_quote_quote() { }
        [Obsolete]
        public mod_quote_quote(String by, String text)
        {
            this.by = by;
            this.text = text;

        }
    }

    /// <summary>
    /// Represents a single quote
    /// </summary>
    [XmlType("mod_quote_multiquote")]
    [Serializable]
    public class mod_quote_multiquote
    {

        public List<mod_quote_quote_line> lines = new List<mod_quote_quote_line>();
        public DateTime on = DateTime.Now;

        internal mod_quote_multiquote() { }
        public mod_quote_multiquote(List<mod_quote_quote_line> lines)
        {
            this.lines = lines;
        }

        public string getText()
        {
            string text = "On " + on.ToString("g") + "\r\n";
            //loop through each line
            foreach (mod_quote_quote_line l in lines)
            {
                text += "*" + l.by  + "* : " + l.text + "\r\n";
            }
            return text;
        }
    }


    /// <summary>
    /// Represents a single quote line
    /// </summary>
    [XmlType("mod_quote_quote_line")]
    [Serializable]
    public class mod_quote_quote_line
    {
        public string by = "";
        public string text = "";
       
        internal mod_quote_quote_line() { }
        public mod_quote_quote_line(String by, String text)
        {
            this.by = by;
            this.text = text;

        }
    }


    public class mod_quote : RobotoModuleTemplate
    {


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
                "quote_conv - Adds a quote containing multiple lines" + "\n\r" +
                "quote - Picks a random quote from the chat's database" + "\n\r" +
                "quote_config - Configure how often to add quotes into chat";
        }
        public override string getWelcomeDescriptions()
        {
            return "Chat quote database - type /quote_add to add a quote to the db, or /quote_config to change settings";
        }


        private bool addQuote(List<mod_quote_quote_line> lines, chat c)
        {
            mod_quote_data localChatData = c.getPluginData<mod_quote_data>();

            if (!quoteExists(lines, c))
            {
                localChatData.multiquotes.Add(new mod_quote_multiquote(lines));
                Roboto.Settings.save();
                return true;
            }
            return false;
        }

        private bool quoteExists(List<mod_quote_quote_line> lines, chat c)
        {
            mod_quote_data localChatData = c.getPluginData<mod_quote_data>();
            foreach (mod_quote_multiquote q in localChatData.multiquotes)
            {
                if (q.lines == lines)
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
            mod_quote_data localChatData = c.getPluginData<mod_quote_data>();
            if (localChatData.multiquotes.Count > 0)
            {

                mod_quote_multiquote q = localChatData.multiquotes[settings.getRandom(localChatData.multiquotes.Count)];

                return (q.getText());
                
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
                mod_quote_data localChatData = (mod_quote_data)c.getPluginData(typeof(mod_quote_data));

                if (m.text_msg.StartsWith("/quote_add"))
                {
                    Messaging.SendQuestion(c.chatID, m.userID,  "Who is the quote by? Or enter 'cancel'", true, typeof(mod_quote), "WHO", m.userFullName, -1, true);
                    processed = true;
                }
                else if (m.text_msg.StartsWith("/quote_conv"))
                {
                    Messaging.SendQuestion(c.chatID, m.userID,  "Enter the first speaker's name, a \\, then the text (e.g. Bob\\I like Bees).\n\rOr enter 'cancel' to cancel", true, typeof(mod_quote), "WHO_M", m.userFullName, -1, true);
                    processed = true;
                }

                else if (m.text_msg.StartsWith("/quote_config"))
                {
                    List<string> options = new List<string>();
                    options.Add("Set Duration");
                    options.Add("Toggle automatic quotes");
                    string keyboard = TelegramAPI.createKeyboard(options, 1);
                    Messaging.SendQuestion(c.chatID, m.userID,
                        "Quotes are currently " + (localChatData.autoQuoteEnabled == true? "enabled" : "disabled") 
                        + " and set to announce every " + localChatData.autoQuoteHours.ToString() + " hours"
                        , false, typeof(mod_quote), "CONFIG", m.userFullName, m.message_id, true, keyboard);
                }
                else if (m.text_msg.StartsWith("/quote"))
                {
                    Messaging.SendMessage(m.chatID, getQuote(c), m.userFullName, true, m.message_id);
                    processed = true;
                }
            }

            //also accept forwarded messages
            


            return processed;
        }

        protected override void backgroundProcessing()
        {
            foreach (chat c in Roboto.Settings.chatData)
            {
                mod_quote_data localChatData = c.getPluginData<mod_quote_data>();
                
                if (localChatData != null && localChatData.autoQuoteEnabled && DateTime.Now > localChatData.nextAutoQuoteAfter && localChatData.multiquotes.Count > 0)
                {
                    Messaging.SendMessage(c.chatID, getQuote(c), null, true);
                    int maxMins = localChatData.autoQuoteHours * 60;
                    //go back 1/8, then add rand 1/4 on
                    int randomMins = settings.getRandom((localChatData.autoQuoteHours * 60) /4);
                    maxMins = maxMins - ( maxMins / 8) + randomMins;
                    localChatData.nextAutoQuoteAfter = DateTime.Now.AddMinutes(maxMins);
                    
                }

            }
        }

        public override bool replyReceived(ExpectedReply e, message m, bool messageFailed = false)
        {
            chat c = Chats.getChat(e.chatID);
            mod_quote_data localChatData = (mod_quote_data)c.getPluginData(typeof(mod_quote_data));

            //Adding quotes
            if (e.messageData.StartsWith("WHO_M"))
            {
                if (m.text_msg.ToLower() == "cancel")
                {
                    Messaging.SendMessage(m.userID, "Cancelled adding a new quote");
                }
                else if (m.text_msg.ToLower() == "done")
                {
                    
                    List<mod_quote_quote_line> lines = new List<mod_quote_quote_line>();
                    //strip the "WHO_M" from the start
                    string message = e.messageData.TrimStart("WHO_M".ToCharArray());
                    //split out the text so we can put into a multiquote object
                    string[] delim = new string[] { "<<#::#>>" };
                    string[] elements = message.Split(delim, StringSplitOptions.None);
                    string last = ""; //toggle between null string (populate with the name), and holding the previous value (add to the list)
                    foreach (string s in elements)
                    {
                        if (last == "") { last = s; }
                        else
                        {
                            lines.Add(new mod_quote_quote_line(last, s));
                            last = "";
                        }
                    }

                    //now add the quote to the db
                    if (lines.Count > 0)
                    {
                        mod_quote_multiquote q = new mod_quote_multiquote(lines);
                        localChatData.multiquotes.Add(q);
                        Messaging.SendMessage(e.chatID, "Added quote \n\r" + q.getText(), m.userFullName, true);

                    }
                    else
                    {
                        Messaging.SendMessage(m.userID, "Couldnt add quote - no lines to add?");
                    }
                    
                }
                else
                {
                    //this should have a "\" in the middle of it to split the user from the text
                    int pos = m.text_msg.IndexOf("\\"[0]);
                    if (pos == -1) { Messaging.SendMessage(m.userID, "Couldn't work out where the name and text were. Cancelled adding a new quote"); }
                    else
                    {
                        //need to store the whole set of messages in messagedata until we are finished
                        string newMsgData = e.messageData;
                        //replace the "\" with something less likely to come up accidentally
                        newMsgData = newMsgData + m.text_msg.Substring(0, pos) + "<<#::#>>" + m.text_msg.Substring(pos+1) + "<<#::#>>";

                        Messaging.SendQuestion(e.chatID, m.userID, "Enter the next line, 'cancel' or 'done'", true, typeof(mod_quote), newMsgData, m.userFullName, m.message_id, true);


                    }


                }


                return true;
            }
            else if (e.messageData == "WHO")
            {
                if (m.text_msg.ToLower() == "cancel")
                {
                    Messaging.SendMessage(m.userID, "Cancelled adding a new quote");
                }
                else
                {
                    Messaging.SendQuestion(e.chatID, m.userID, "What was the quote from " + m.text_msg, true, typeof(mod_quote), "TEXT " + m.text_msg, m.userFullName, m.message_id, true, "", false,false,true);
                }
                return true;
            }

            


            else if (e.messageData.StartsWith("TEXT"))
            {
                string quoteBy = e.messageData.Substring(5);    //.TrimStart("TEXT ".ToCharArray());
                bool success = addQuote(new List<mod_quote_quote_line>() { new mod_quote_quote_line(quoteBy, m.text_msg) }, c);
                Messaging.SendMessage(e.chatID, "Added " + m.text_msg + " by " + quoteBy + " " + (success ? "successfully" : "but fell on my ass"));
                return true;
            }

            //CONFIG
            else if (e.messageData.StartsWith("CONFIG"))
            {
                if (m.text_msg == "Set Duration")
                {
                    Messaging.SendQuestion(e.chatID, m.userID, "How long between updates?" + m.text_msg, false, typeof(mod_quote), "DURATION" + m.text_msg, m.userFullName, m.message_id, true);
                    return true;
                }
                else if (m.text_msg == "Toggle automatic quotes")
                {
                    localChatData.autoQuoteEnabled = !localChatData.autoQuoteEnabled;
                    Messaging.SendMessage(c.chatID, "Quotes are now " + (localChatData.autoQuoteEnabled == true ? "enabled" : "disabled"), m.userFullName, false, -1, true);
                    return true;
                }
            }

            //DURATION
            else if (e.messageData.StartsWith("DURATION"))
            {
                int hours = -1;
                if (int.TryParse(m.text_msg, out hours) && hours >= -1)
                {
                    localChatData.autoQuoteHours = hours;
                    Messaging.SendMessage(c.chatID, "Quote schedule set to every " + hours.ToString() + " hours.", m.userFullName, false, -1, true);
                }
                else if (m.text_msg != "Cancel")
                {
                    Messaging.SendQuestion(e.chatID, m.userID, "Not a number. How many hours between updates, or 'Cancel' to cancel" + m.text_msg ,false, typeof(mod_quote), "DURATION" + m.text_msg, m.userFullName, m.message_id, true);
                }
                return true;

            }
            return false;
        }
    }
}
