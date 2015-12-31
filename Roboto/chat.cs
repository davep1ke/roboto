using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Roboto
{
    /// <summary>
    /// represents a group chat and all the data stored abotu it. Doesnt exist for PERSON chats, just group
    /// </summary>
    public class chat
    {
        public long chatID;
        public bool enabled = false; //assume chats are disabled until we get a /start

        public List<Modules.RobotoModuleChatDataTemplate> chatData = new List<Modules.RobotoModuleChatDataTemplate>();

        internal chat() { }

        public chat(long chatID)
        {
            this.chatID = chatID;
            initPlugins();
        }

        /// <summary>
        /// Loop through the plugins and get them to populate stub data for this chat, if they need to
        /// </summary>
        public void initPlugins()
        {
            foreach (Modules.RobotoModuleTemplate plugin in settings.plugins)
            {
                plugin.initChatData(this);
                plugin.validateChatData(this);
                //find and validate the data
            }

        }

        public void enable()
        {
            enabled = true;
        }
        public void disable()
        {
            enabled = false;
        }

        public void addChatData(Modules.RobotoModuleChatDataTemplate data)
        {
            data.chatID = this.chatID;
            //replace current Chat data, or add if doesnt exist
            Modules.RobotoModuleChatDataTemplate found = null;
            foreach (Modules.RobotoModuleChatDataTemplate current in chatData)
            {
                if (current.GetType() == data.GetType())
                {
                    found = current;
                }
            }
            if (found != null)
            {
                Console.WriteLine("Chat data Already exists!");
                throw new InvalidOperationException("Chat data Already exists!");
            }

            chatData.Add(data);
        }

        public T getPluginData<T>()
        {
            foreach (Modules.RobotoModuleChatDataTemplate existing in chatData)
            {
                if (existing.GetType() == typeof(T))
                {
                    //Console.WriteLine("Plugin data of type " + data.GetType().ToString() + " already exists!");
                    T retVal = (T)Convert.ChangeType(existing, typeof(T));
                    return retVal;
                }
            }

            Console.WriteLine("Couldnt find plugin data of type " + typeof(T).ToString());
            return default(T);
        }


        internal Modules.RobotoModuleChatDataTemplate getPluginData(Type t)
        {
            foreach (Modules.RobotoModuleChatDataTemplate existing in chatData)
            {
                if (existing.GetType() == t)
                {
                    return existing;
                }
            }

            Console.WriteLine("Couldnt find plugin data of type " + t.ToString());
            return null;
        }
    }
}
