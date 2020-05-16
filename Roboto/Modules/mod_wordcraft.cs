using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace RobotoChatBot.Modules
{
    /// <summary>
    /// Data to be stored in the XML store
    /// </summary>
    [XmlType("mod_wordcraft_data")]
    [Serializable]
    public class mod_wordcraft_data : RobotoModuleDataTemplate
    {
        public List<String> words = new List<String>()
        {
              "Bilge","Rabbit","Moose","Ramp","Clown","Glimp","Hop","Mop"
        };
    }

    
    public class mod_wordcraft : RobotoModuleTemplate
    {
      
        public override void init()
        {
            pluginDataType = typeof(mod_wordcraft_data);

            chatHook = true;
            chatEvenIfAlreadyMatched = false;
            chatPriority = 5;

        }

        public override string getMethodDescriptions()
        {
            return
                "craft - Crafts a random phrase badger" + "\n\r" +
                "craft_add - Adds a birthday reminder for the current chat" + "\n\r" +
                "craft_remove - Removes a birthday reminder for the current chat"
                ;
        }

        public override string getWelcomeDescriptions()
        {
            return "Nonsense word generator. Try /craft!";

        }

      
        public override bool chatEvent(message m, chat c = null)
        {
            bool processed = false;

            if (m.text_msg.StartsWith("/craft_add"))
            {
                Messaging.SendQuestion(m.chatID, m.userID, "Enter the word to add", true, GetType(), "AddWord");
                processed = true;
            }
            else if (m.text_msg.StartsWith("/craft_remove"))
            {
                Messaging.SendQuestion(m.chatID, m.userID, "Enter the word to remove", true, GetType(), "RemWord");
                processed = true;
            }
            else if (m.text_msg.StartsWith("/craft"))
            {
                Messaging.SendMessage(m.chatID, craftWord());
                processed = true;
            }
           
            return processed;
        }


        public override bool replyReceived(ExpectedReply e, message m, bool messageFailed = false)
        {
            if (e.messageData == "AddWord")
            {
                addCraftWord(m.text_msg);
                Messaging.SendMessage(m.chatID, "Added " + m.text_msg + " for " + m.userFirstName);
                return true;
            }
            
            else if (e.messageData == "RemWord")
            {
                bool success = removeCraftWord(m.text_msg);
                Messaging.SendMessage(m.chatID, "Removed " + m.text_msg + " for " + m.userFirstName + " " + (success ? "successfully" : "but fell on my ass"));
                return true;
            }
            
            return false;
        }

       

        public void addCraftWord(string word)
        {
            ((mod_wordcraft_data)localData).words.Add(word);
        }

        public bool removeCraftWord(string word)
        {
            return ((mod_wordcraft_data)localData).words.Remove(word);
        }

        public String craftWord()
        {
            String result = "";
            List<String> pickedWords = new List<string>();
            //pick a random word


            int words = settings.getRandom(4) + 1;
            for (int i = 0; i < words; i++)
            {
                int wordID = settings.getRandom(((mod_wordcraft_data)localData).words.Count);
                String word = ((mod_wordcraft_data)localData).words[wordID];
                if (!pickedWords.Contains(word))
                {
                    if (result != "") { result += " "; }
                    result += word;
                    pickedWords.Add(word);
                }
            }


            //add a number like 20% of the time
            if (settings.getRandom(100) < 20)
            {
                result += " " + settings.getRandom(9).ToString();
                //add another number like 10% of the time (so 2 digit
                if (settings.getRandom(100) < 10)
                {
                    result += settings.getRandom(9).ToString();
                }

                //add a 0 like 20% of the time a numbers been added
                if (settings.getRandom(100) < 20)
                {
                    result += "0";
                    //add another 0 like 70% of the time we have a nr and a 0
                    if (settings.getRandom(100) < 70)
                    {
                        result += "0";
                    }

                }
            }
            return result;

        }
        
    }
}
