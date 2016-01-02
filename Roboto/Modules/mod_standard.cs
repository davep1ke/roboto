using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace Roboto.Modules
{


    public class mod_standard : RobotoModuleTemplate
    {

        public override void init()
        {
            pluginDataType = null;

            chatHook = true;
            chatEvenIfAlreadyMatched = false;
            chatPriority = 1;

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
        
        public override bool chatEvent(message m, chat c = null)
        {
            bool processed = false;

            if (m.text_msg.StartsWith("/help"))
            {
                TelegramAPI.SendMessage(m.chatID, getAllMethodDescriptions());
                processed = true;
            }
            else if (m.text_msg.StartsWith("/save"))
            {
                Roboto.Settings.save();
                TelegramAPI.SendMessage(m.chatID, "Saved settings");
            }
            else if (m.text_msg.StartsWith("/stats"))
            {
                TimeSpan uptime = DateTime.Now.Subtract(Roboto.startTime);

                String statstxt = "I is *@" + Roboto.Settings.botUserName + "*" + "\n\r"+
                    "Uptime: " +  uptime.Days.ToString() + " days, " + uptime.Hours.ToString() + " hours and " + uptime.Minutes.ToString() + " minutes." + "\n\r" + 
                    "I currently know about " + Roboto.Settings.chatData.Count().ToString() + " chats." + "\n\r" +
                    "The following plugins are currently loaded:" + "\n\r";

                foreach (RobotoModuleTemplate plugin in settings.plugins )
                {
                    statstxt += "*" + plugin.GetType().ToString() + "*" + "\n\r";
                    statstxt += plugin.getStats() + "\n\r";
                }

                TelegramAPI.SendMessage(m.chatID, statstxt,true);
            }

            //TODO - start, stop listening to chat. 


            return processed;
        }
        
    }
}
