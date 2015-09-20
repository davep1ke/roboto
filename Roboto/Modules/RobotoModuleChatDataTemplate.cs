using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Roboto.Modules
{
    public abstract class RobotoModuleChatDataTemplate
    {
        public int chatID = -1;
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
    }
}
