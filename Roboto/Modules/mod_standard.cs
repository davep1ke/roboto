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
    [XmlType("mod_standard_data")]
    [Serializable]
    public class mod_standard_data : RobotoModuleDataTemplate
    {
        internal mod_standard_data() { }
    }
 
    public class mod_standard : RobotoModuleTemplate
    {

        public override void init()
        {
            pluginDataType = typeof(mod_standard_data);

            chatHook = true;
            chatEvenIfAlreadyMatched = false;
            chatPriority = 5;

        }

        public override string getMethodDescriptions()
        {
            return
                "help - Returns this list of commands" + "\n\r" +
                "start - Starts listening to the chat" + "\n\r" +
                "stop - Stops listening to the chat, until a START is entered." + "\n\r" +
                "save - Saves any outstanding in memory stuff to disk."
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

        public override void initData()
        {
           //no data!

        }

        public override void initChatData()
        {
            
        }

       

        public override bool chatEvent(message m)
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
            //TODO - start, stop listening to chat. 

            
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
