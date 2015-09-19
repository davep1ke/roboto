using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Roboto
{
    /// <summary>
    /// represents a chat and all the data stored abotu it
    /// </summary>
    public class chat
    {
        public enum chatType { user, chat };
        public chatType type;
        public int chatID;

        public List<Modules.RobotoModuleChatDataTemplate> chatData = new List<Modules.RobotoModuleChatDataTemplate>();

        internal chat() { }

        public chat(chatType type, int chatID)
        {
            this.type = type;
            this.chatID = chatID;
        }

        public void addChatData(Modules.RobotoModuleChatDataTemplate data)
        {
            //replace current Chat data, or add if doesnt exist
            Modules.RobotoModuleChatDataTemplate found = null;
            foreach (Modules.RobotoModuleChatDataTemplate current in chatData)
            {
                if (current.pluginType == data.GetType())
                {
                    found = current;
                }
            }
            if (found != null)
            {
                chatData.Remove(found);
            }

            chatData.Add(data);
        }

    }
}
