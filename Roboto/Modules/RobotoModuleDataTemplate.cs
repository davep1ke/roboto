using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Roboto.Modules
{
    public abstract class RobotoModuleDataTemplate
    {
        public DateTime lastBackgroundUpdate = DateTime.MinValue;
        internal RobotoModuleDataTemplate() { } //for serialisation
    }
}
