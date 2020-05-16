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
    /// Represents a xyzzy player
    /// </summary>
    public class mod_xyzzy_player
    {
        public string name;

        public string name_markdownsafe
        {
            get
            {
                return Helpers.common.removeMarkDownChars(name);
            }
        }


        public bool fuckedWith = false;
        public string handle = "";
        public long playerID;
        public int wins = 0;
        public List<String> cardsInHand = new List<string>();
        public List<String> selectedCards = new List<string>();
        internal mod_xyzzy_player() { }
        public mod_xyzzy_player(string name, string handle, long playerID)
        {
            this.name = name;
            this.handle = handle;
            this.playerID = playerID;
        }

        public override string ToString()
        {

            string response = " " + name;
            if (handle != "") { response += " (@" + handle + ")"; }

            return response;
        }

        public string ToString(bool markdownSafe)
        {
            if (markdownSafe)
            {
                string response = " " + name_markdownsafe;
                String handle_safe = Helpers.common.removeMarkDownChars(handle);
                if (handle != "" && handle == handle_safe)
                {
                    response += " (@" + handle_safe + ")";
                }
                else if (handle != handle_safe)
                {
                    Roboto.log.log("Skipping handle for " + handle + " as contains markdown", logging.loglevel.low);
                }
                return response;
            }
            else
            {
                return ToString();
            }
        }

        internal void topUpCards(int nrCards, List<string> availableAnswers, long chatID)
        {

            while (cardsInHand.Count < nrCards)
            {
                //have we reached the end of the pack?
                if (availableAnswers.Count == 0)
                {
                    //get the chatData and top up the cards. 
                    mod_xyzzy_chatdata chatData = (mod_xyzzy_chatdata)Chats.getChat(chatID).getPluginData(typeof(mod_xyzzy_chatdata));
                    chatData.addAllAnswers();
                    Messaging.SendMessage(chatID, "All answers have been used up, pack has been refilled!");
                }

                //pick a card
                string cardUID = availableAnswers[settings.getRandom(availableAnswers.Count)];
                cardsInHand.Add(cardUID);

                //remove it from the available list
                availableAnswers.Remove(cardUID);
            }
        }



        public bool SelectAnswerCard(string cardUID)
        {
            bool success = cardsInHand.Remove(cardUID);
            if (success)
            {
                selectedCards.Add(cardUID);
            }
            return success;

        }

        public string getAnswerKeyboard(mod_xyzzy_coredata localData)
        {
            List<string> answers = new List<string>();

            List<string> invalidCards = new List<string>();
            foreach (string cardID in cardsInHand)
            {
                mod_xyzzy_card c = localData.getAnswerCard(cardID);
                if (c != null)
                {

                    answers.Add(c.text);
                }
                else
                {
                    Roboto.log.log("Answer card " + cardID + " not found! Removing from " + name + "'s hand", logging.loglevel.critical);
                    invalidCards.Add(cardID);
                }
            }
            //remove any invalid cards
            foreach (string cardID in invalidCards) { cardsInHand.Remove(cardID); }

            return (TelegramAPI.createKeyboard(answers, 1));
        }

        public void toggleFuckWith()
        {

            if (fuckedWith == true) { fuckedWith = false; }
            else { fuckedWith = true; }
        }

        public string getPointsMessage()
        {

            string response = "\n\r" + name_markdownsafe + " - ";
            if (!fuckedWith) { return response + wins + " points."; }
            else
            {
                string[] suffixes = { "INT", "XP", "Points", "Sq. Ft.", "ft, 6 inches", "mm", "out of 10. Must try harder.", "Buzzards", "Buzzards/m/s²", "m/s²" };

                //want a multipler between -1 and 0.5.
                float multiplier = (50 - settings.getRandom(150)) / 100f;
                int randomSuffix = settings.getRandom(suffixes.Count() - 1);
                int newscore = Convert.ToInt32(wins * multiplier);
                response += newscore.ToString() + " " + suffixes[randomSuffix];

            }
            return response;
        }

        public bool setScore(int playerScore)
        {
            Roboto.log.log("Overwrote " + this.ToString() + "'s points with " + playerScore, logging.loglevel.warn);
            wins = playerScore;
            return true;
        }
    }


    /// <summary>
    /// Represents a xyzzy card
    /// </summary>
    public class mod_xyzzy_card
    {
        public string uniqueID = Guid.NewGuid().ToString();
        public String text;
        [System.Obsolete("use Pack (Guid)")]
        public String category; //what pack did the card come from

        //shitty workaround to allow us to load in the cateogry info temporarily. - http://stackoverflow.com/questions/5096926/what-is-the-get-set-syntax-in-c
        [XmlElement("category")]
        public string TempCategory
        {
#pragma warning disable 612, 618
            get { return category; }
            set { category = value; }
#pragma warning restore 612, 618
        }

        public Guid packID;
        public int nrAnswers = -1;

        internal mod_xyzzy_card() { }
        public mod_xyzzy_card(String text, Guid packID, int nrAnswers = -1)
        {
            this.text = text;
            this.packID = packID;
            this.nrAnswers = nrAnswers;
        }

        public override string ToString()
        {
            return text;
        }

    }
}
