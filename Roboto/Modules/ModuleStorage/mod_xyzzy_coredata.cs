using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace Roboto.Modules
{
    /// <summary>
    /// General Data to be stored in the plugin XML store.
    /// </summary>
    [XmlType("mod_xyzzy_coredata")]
    [Serializable]
    public class mod_xyzzy_coredata : RobotoModuleDataTemplate
    {
        public DateTime lastDayProcessed = DateTime.MinValue;
        public List<mod_xyzzy_card> questions = new List<mod_xyzzy_card>();
        public List<mod_xyzzy_card> answers = new List<mod_xyzzy_card>();
        //removed - moved into telegramAPI and settings class. 
        //public List<mod_xyzzy_expectedReply> expectedReplies = new List<mod_xyzzy_expectedReply>(); //replies expected by the various chats
        //internal mod_xyzzy_coredata() { }

        

        public mod_xyzzy_card getQuestionCard(string cardUID)
        {
            foreach (mod_xyzzy_card c in questions)
            {
                if (c.uniqueID == cardUID) { return c; }
            }
            return null;
        }

        public mod_xyzzy_card getAnswerCard(string cardUID)
        {
            foreach (mod_xyzzy_card c in answers)
            {
                if (c.uniqueID == cardUID)
                {
                    return c;
                }
            }
            return null;
        }

        public List<String> getPackFilterList()
        {
            //include "all"
            List<String> packs = new List<string>();
            foreach (mod_xyzzy_card q in questions)
            {
                packs.Add(q.category.Trim());
            }
            foreach (mod_xyzzy_card a in answers)
            {
                packs.Add(a.category.Trim());
            }
            return packs.Distinct().ToList();
        }

    }
}
