using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace Roboto
{
    public class settings
    {

        public class quote
        {
            public string by = "";
            public string text = "";
            public DateTime on = DateTime.Now;

            internal quote(){}
            public quote(String by, String text)
            {
                this.by = by;
                this.text = text;

            }
        }
        
        public List<replacement> replacements = new List<replacement>();

        public string telegramAPIURL;
        public string telegramAPIKey;
        public string botUserName = "";
        public int waitDuration = 60; //wait duration for long polling. 
        public int lastUpdate = 0; //last update index, needs to be passed back with each call. 
        public int chatID = 0; //id for the chat to send/recieve from

        //word craft storage
        public List<String> craft_words = new List<string>();
        public List<quote> quotes = new List<quote>();



        //stuff
        Random randGen = new Random();

        /// <summary>
        /// Makes sure there is some sample data in place. 
        /// </summary>
        public void validate()
        {
            if (craft_words.Count < 5)
            {
                addCraftWord("Bilge");
                addCraftWord("Rabbit");
                addCraftWord("Moose");
                addCraftWord("Ramp");
                addCraftWord("Clown");
                addCraftWord("Glimp");
                addCraftWord("Hop");
                addCraftWord("Mop");
            }

            if (botUserName == "") { botUserName = "Roboto_its_alive_bot"; }

        }


        public static settings load()
        {
            try
            {
                XmlSerializer deserializer = new XmlSerializer(typeof(settings));
                TextReader textReader = new StreamReader(@"sett.ings");
                settings setts = (settings)deserializer.Deserialize(textReader);
                textReader.Close();
                return setts;
            }


            catch (System.IO.FileNotFoundException)
            {
                //create a new one
                settings sets = new settings();

                #region populate defaults
                // TODO remove
                sets.telegramAPIURL = "https://api.telegram.org/bot";
                sets.telegramAPIKey = "137327694:AAGqvSh1YEAN_GgHYf9Cx0rFwuX6uXufhNU";
                
                //sets.replacements.Add(new replacement(@"F:\eps\", @"\\davepine\eps2\"));
                //sets.replacements.Add(new replacement(@"D:\eps\", @"\\davepine\eps\"));

                #endregion
                return sets;

            }



                

            catch (Exception e)
            {
                string n = e.ToString();
            }

            return null;

        }

        public void save()
        {

            XmlSerializer serializer = new XmlSerializer(typeof(settings));
            TextWriter textWriter = new StreamWriter(@"sett.ings");
            serializer.Serialize(textWriter, this);
            textWriter.Close();
        }

        public void addCraftWord(string word)
        {
            craft_words.Add(word);
        }

        public bool removeCraftWord(string word)
        {
           return craft_words.Remove(word);
        }

        public String craftWord()
        {
            String result = "";
            List<String> pickedWords = new List<string>();
            //pick a random word
            

            int words = randGen.Next(4) + 1;
            for (int i = 0; i < words; i++)
            {
                int wordID = randGen.Next(craft_words.Count);
                String word = craft_words[wordID];
                if (!pickedWords.Contains(word))
                {
                    if (result != "") { result += " "; }
                    result += word;
                    pickedWords.Add(word);
                }
            }


            //add a number like 20% of the time
            if (randGen.Next(100) < 20)
            {
                result += " " + randGen.Next(9).ToString();
                //add another number like 10% of the time (so 2 digit
                if (randGen.Next(100) < 10)
                {
                    result += randGen.Next(9).ToString();
                }

                //add a 0 like 20% of the time a numbers been added
                if (randGen.Next(100) < 20)
                {
                    result += "0";
                    //add another 0 like 70% of the time we have a nr and a 0
                    if (randGen.Next(100) < 70)
                    {
                        result += "0";
                    }

                }
            }
            return result;

        }

        public bool addQuote(string by, string text)
        {
            if (!quoteExists(by, text))
            {
                quotes.Add(new quote(by, text));
                save();
                return true;
            }
            return false;

        }

        public bool quoteExists(string by, string text)
        {
            foreach (quote q in quotes)
            {
                if (q.by == by && q.text == text)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// gets a FORMATTED string for the quote
        /// </summary>
        /// <returns></returns>
        public string getQuote()
        {
            if (quotes.Count > 0)
            {
                quote q = quotes[randGen.Next(quotes.Count)];
                return(
                    "*" + q.by + "* said \r\n" +
                    q.text + "\r\n" +
                    "on " + q.on.ToString("g"));
            }
            else
            {
                return "No quotes in DB";
            }
        }

        public int getUpdateID()
        {
            return lastUpdate + 1;
        }
    }



}
