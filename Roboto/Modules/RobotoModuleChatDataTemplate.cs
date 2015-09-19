using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Roboto.Modules
{
    public abstract class RobotoModuleChatDataTemplate
    {
        public Type pluginType;
        internal RobotoModuleChatDataTemplate() {} //for serialisation
    }
}
