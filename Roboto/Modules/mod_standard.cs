using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace Roboto.Modules
{
    [XmlType("mod_standard_data")]
    [Serializable]
    public class mod_standard_data : RobotoModuleDataTemplate
    {

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

            backgroundHook = true;
            backgroundMins = 30;
            

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

        public override string getMethodDescriptions()
        {
            return
                "help - Returns this list of commands" + "\n\r" +
                "start - Starts listening to the chat" + "\n\r" +
                "stop - Stops listening to the chat, until a START is entered." + "\n\r" +
                "save - Saves any outstanding in memory stuff to disk." + "\n\r" +
                "stats - Returns an overview of the currently loaded plugins.";
                ;
        }

        public static String getAllMethodDescriptions()
        {
            String methods = "The following methods are supported:";
            foreach (RobotoModuleTemplate plugin in settings.plugins)
            {
                methods += "\n\r" + plugin.getMethodDescriptions(); 
            }
            return methods;
        }

        /// <summary>
        /// Background processing for Roboto
        /// </summary>
        protected override void backgroundProcessing()
        {
            Roboto.Settings.stats.houseKeeping();

        }

        public override bool chatEvent(message m, chat c = null)
        {
            bool processed = false;

            if (m.text_msg.StartsWith("/help")
                || m.text_msg.StartsWith ("/start") && c != null && c.muted == false)
            {
                TelegramAPI.SendMessage(m.chatID, getAllMethodDescriptions());
                processed = true;
            }
            else if (m.text_msg.StartsWith("/save"))
            {
                Roboto.Settings.save();
                TelegramAPI.SendMessage(m.chatID, "Saved settings");
            }
            else if (m.text_msg.StartsWith("/stop") && c != null)
            {
                c.muted = true;
                TelegramAPI.SendMessage(m.chatID, "OK, I am now ignoring all messages in this chat until I get a /start command. ");
            }
            else if (m.text_msg.StartsWith("/start") && c != null && c.muted == true)
            {
                c.muted = false;
                TelegramAPI.SendMessage(m.chatID, "I am back. Type /help for a list of commands.");
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

                TelegramAPI.SendMessage(m.chatID, statstxt, true);
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
                TelegramAPI.SendPhoto(m.chatID, "Stats", image, "StatsGraph.jpg", "application/octet-stream", m.message_id , false);


                //TODO - or get a keyboard
            }
                //TODO - start, stop listening to chat. 


                return processed;
        }

               
        
    }
}
