using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Roboto.Modules
{
    public abstract class RobotoModuleChatDataTemplate
    {
        public long chatID = -1;

        internal RobotoModuleChatDataTemplate() {} //for serialisation
        protected RobotoModuleChatDataTemplate(int chatID)
        {
            this.chatID = chatID;
        }
        public bool isValid()
        {
            if (chatID == -1) { return false; }
            return true;
        }

        public chat getChat()
        {
            return Roboto.Settings.getChat(chatID);

        }
        
        /// <summary>
        /// Logging wrapper
        /// </summary>
        /// <param name="text"></param>
        /// <param name="level"></param>
        /// <param name="colour"></param>
        /// <param name="noLineBreak"></param>
        public void log(string text, logging.loglevel level = logging.loglevel.normal, ConsoleColor colour = ConsoleColor.White, bool noLineBreak = false)
        {
            Roboto.log.log(text, level, colour, noLineBreak, false, false, false, 2);
        }

        public virtual bool isPurgable()
        {
            //by default, assume everything is purgeable
            return true;

        }

            
    }
}
