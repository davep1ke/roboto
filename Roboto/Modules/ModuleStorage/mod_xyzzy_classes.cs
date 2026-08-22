using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using System.Text.Json.Serialization;


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

        /// <summary>Added via "Add Bots" (mod_xyzzy_chatdata.addBots) - not a legacy feature at
        /// all, legacy never had non-human players. Carried forward from the abandoned rewrite
        /// branch's own "Add Bots" (see MIGRATION.md phase 9). playerID for a bot is a synthetic
        /// negative value (real Telegram user IDs are always positive), an unambiguous "never try
        /// to DM this" signal wherever askQuestion/beginJudging would otherwise message a player.</summary>
        public bool isBot = false;

        // System.Text.Json's default reflection-based converter only auto-uses a constructor when
        // there's a public parameterless one, or exactly one public parameterized one - with two
        // public parameterized constructors and only an internal parameterless one, it has no
        // usable candidate at all and throws NotSupportedException on every deserialize attempt.
        // Found while building phase 8's migrator test coverage: no existing test had ever exercised
        // a real save()-then-reload round trip with a populated players list, so this was invisible
        // even though it's a genuine, serious bug in phase 3's SqliteStateStore persistence layer -
        // any chat with real players in its xyzzy game would crash settings.load() entirely (not
        // just lose that chat's data - the exception propagates past the whole load(), so it takes
        // the bot's startup down) on any restart. [JsonConstructor] tells STJ to use this one
        // explicitly rather than trying to infer among the ambiguous candidates.
        [JsonConstructor]
        internal mod_xyzzy_player() { }
        public mod_xyzzy_player(string name, string handle, long playerID)
        {
            this.name = name;
            this.handle = handle;
            this.playerID = playerID;
        }

        public mod_xyzzy_player(string name, long playerID, bool isBot)
        {
            this.name = name;
            this.handle = "";
            this.playerID = playerID;
            this.isBot = isBot;
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

        public List<List<string>> getAnswerKeyboard(mod_xyzzy_coredata localData)
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
        [XmlIgnore]
        public String category; //what pack did the card come from

        //shitty workaround to allow us to load in the cateogry info temporarily. - http://stackoverflow.com/questions/5096926/what-is-the-get-set-syntax-in-c
        //
        // [XmlIgnore] added on category (above) when porting off .NET Framework: modern .NET's
        // XmlSerializer is stricter about duplicate element names than the old Framework one was -
        // both the category field and this TempCategory property were serializing under the same
        // "category" XML element name (neither had XmlIgnore), which used to be silently tolerated
        // but now throws InvalidOperationException("...already present in the current scope...") at
        // XmlSerializer construction. TempCategory is the only one meant to actually serialize -
        // category is just its obsolete-but-still-read backing field (see mod_xyzzy_coredata.cs's
        // dedup/startup-check code, which still compares against it) - so this is a compatibility fix,
        // not a behavior change: the same data still round-trips through the same "category" element.
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
