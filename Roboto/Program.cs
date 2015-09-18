
using System.Text;
using System;
//using System.Web;
using System.Net;
using System.IO;
using System.Runtime.Serialization;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json;  
using Newtonsoft.Json.Linq;

namespace Roboto
{
    public class Roboto
    {
        private static bool endLoop = false;
        public static settings Settings;

        static void Main(string[] args)
        {
            Settings = settings.load();
            Settings.validate();
            Roboto.Process();
            Settings.save();

        }

        //Commands
        /*
         * 
BotFather:

/setcommands
         
craft - Wordcraft something
craft_add - Adds a word to the craft dictionary (prompts)
craft_remove - Removes a word from the craft dictionary (prompts)
quote - Says a quote
addquote - Adds a quote to the quote DB (prompts)
save - saves quotes and lists
          
          
         */

        private static void Process()
        {
            

            while (!endLoop)
            {
                string updateURL = Settings.telegramAPIURL + Settings.telegramAPIKey + "/getUpdates" +
                    "?offset=" + Settings.getUpdateID() +
                    "&timeout=" + Settings.waitDuration +
                    "&limit=5";

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(updateURL);
                Console.WriteLine("getUpdate, ID " + Settings.getUpdateID());
                request.Method = "GET";
                request.ContentType = "application/json";
                
                try
                {
                    WebResponse webResponse = request.GetResponse();
                    using (Stream webStream = webResponse.GetResponseStream())
                    {
                        if (webStream != null)
                        {
                            using (StreamReader responseReader = new StreamReader(webStream))
                            {
                                string response = responseReader.ReadToEnd();

                                JObject jo = JObject.Parse(response);
                                
                                //success?
                                string path = jo.First.Path;
                                string result = jo.First.First.Value<string>();


                                if (path != "ok" || result != "True")
                                {
                                    endLoop = true;
                                    //throw new WebException("Failure code from web service");

                                }
                                else
                                {
                                    //open the response and parse it using JSON. Probably only one result, but should be more? 
                                    foreach (JToken token in jo.SelectTokens("result.[*]"))//jo.Children()) //) records[*].data.importedPath"
                                    {

                                        //Sample Message looks like this:
                                        /*
                                          "update_id": 824722492,
                                          "message": {
                                                "message_id": 2,
                                                "from": {
                                                      "id": 120498152,
                                                      "first_name": "Phil",
                                                      "last_name": "Hornby"
                                                },
                                                "chat": {
                                                      "id": 120498152,
                                                      "first_name": "Phil",
                                                      "last_name": "Hornby"
                                                },
                                                "date": 1441754934,
                                                "text": "/start"
                                             }
                                         */

                                        Console.WriteLine(token.ToString());
                                        
                                        //TOP LEVEL TOKENS
                                        JToken updateID_TK = token.First;
                                        JToken update_TK = updateID_TK.Next.First;

                                        int updateID = updateID_TK.First.Value<int>();
                                        //TODO - make live - remove the increment.
                                        Settings.lastUpdate = updateID;

                                        if (update_TK.Path == "result[0].message" && update_TK.SelectToken(".text") != null)
                                        {
                                            //get the text
                                            JToken replyMsg_TK = update_TK.SelectToken(".reply_to_message");
                                            int message_id = update_TK.SelectToken(".message_id").Value<int>();
                                            int chat_id = update_TK.SelectToken(".chat.id").Value<int>();
                                            
                                            String text_msg = update_TK.SelectToken(".text").Value<String>();
                                            String userFirstName = update_TK.SelectToken(".from.first_name").Value<String>();
                                            

                                            //is this in reply to another text that we sent? 
                                            String replyOrigMessage = "";
                                            String replyOrigUser = "";
                                            int replyMessageID = -1;

                                            if (replyMsg_TK != null)
                                            {
                                                replyOrigMessage = replyMsg_TK.SelectToken(".text").Value<String>();
                                                replyOrigUser = replyMsg_TK.SelectToken(".from.username").Value<String>();
                                                replyMessageID = replyMsg_TK.SelectToken(".message_id").Value<int>();
                                            }




                                            #region actions
                                            //now decide what to do with this stuff.


                                                #region wordcraft
                                                //wordcraft
                                                if (text_msg.StartsWith("/craft_add"))
                                                {
                                                    GetReply(chat_id, "Enter the word to add", message_id, true);
                                                }
                                                else if (text_msg.StartsWith("/craft_remove"))
                                                {
                                                    GetReply(chat_id, "Enter the word to remove", message_id, true);
                                                }
                                                else if (text_msg.StartsWith("/craft"))
                                                {
                                                    SendMessage(chat_id, Settings.craftWord());
                                                }
                                                else if (replyMsg_TK != null && replyOrigMessage == "Enter the word to add" && replyOrigUser == Settings.botUserName)
                                                {
                                                    //reply to add word
                                                    Settings.addCraftWord(text_msg);
                                                    SendMessage(chat_id, "Added " + text_msg + " for " + userFirstName );
                                                }
                                                else if (replyMsg_TK != null && replyOrigMessage == "Enter the word to remove" && replyOrigUser == Settings.botUserName)
                                                {
                                                    bool success =  Settings.removeCraftWord(text_msg);
                                                    SendMessage(chat_id, "Removed " + text_msg + " for " + userFirstName + " " + (success?"successfully":"but fell on my ass"));
                                                }
                                                #endregion
                                                #region chatquotes
                                                else if (text_msg.StartsWith("/addquote"))
                                                {
                                                    GetReply(chat_id, "Who is the quote by", message_id, true);
                                                }
                                                else if (replyMsg_TK != null && replyOrigMessage == "Who is the quote by" && replyOrigUser == Settings.botUserName)
                                                {
                                                    GetReply(chat_id, "What was the quote from " + text_msg, message_id, true);
                                                }
                                                else if (replyMsg_TK != null && replyOrigMessage.StartsWith("What was the quote from ") && replyOrigUser == Settings.botUserName)
                                                {
                                                    string quoteBy = replyOrigMessage.Replace("What was the quote from ", "");
                                                    bool success = Settings.addQuote(quoteBy, text_msg);
                                                    SendMessage(chat_id, "Added " + text_msg + " by " + quoteBy + " " + (success ? "successfully" : "but fell on my ass"));
                                                }
                                                else if (text_msg.StartsWith("/quote"))
                                                {
                                                    SendMessage(chat_id, Settings.getQuote(), true, message_id);
                                                }
                                                #endregion
                                                #region save
                                                else if (text_msg.StartsWith("/save"))
                                                {
                                                    Settings.save();
                                                    SendMessage(chat_id, "Saved settings");
                                                }
                                                #endregion
                                                #region dummy reply
                                                //some processing
                                                else if (text_msg.Contains("Roboto"))
                                                {
                                                    SendMessage(chat_id, "Hobble mop flimp scab");
                                                }
                                                #endregion
                                            #endregion
                                        }
                                        //dont know what other update types we want to monitor? 

                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.Out.WriteLine("-----------------");
                    Console.Out.WriteLine(e.Message);
                }
            }
        }

        private static void SendMessage(int chat_id, string text, bool markDown = false, int replyToMessageID = -1)
        {

            string postURL = Settings.telegramAPIURL + Settings.telegramAPIKey + "/sendMessage" +
                   "?chat_id=" + chat_id +
                   "&text=" + text;
            if (replyToMessageID != -1){postURL += "&reply_to_message_id=" + replyToMessageID; }
            if (markDown == true) { postURL += "&parse_mode=Markdown"; }

            sendPOST(postURL);

        }


        private static void GetReply(int chat_id, string text, int replyToMessageID = -1, bool selective = false)
        {

            string postURL = Settings.telegramAPIURL + Settings.telegramAPIKey + "/sendMessage" +
                   "?chat_id=" + chat_id +
                   "&text=" + text +  
                   "&reply_markup={\"force_reply\":true,\"selective\":" + selective.ToString().ToLower() + "}";

            if (replyToMessageID != -1) { postURL += "&reply_to_message_id=" + replyToMessageID; }
            //TODO - should URLEncode the text.
            sendPOST(postURL);

        }
        private static void sendPOST(String postURL)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(postURL);

            request.Method = "POST";
            request.ContentType = "application/json";
            //request.ContentLength = DATA.Length;


            WebResponse webResponse = request.GetResponse();
            using (Stream webStream = webResponse.GetResponseStream())
            {
                if (webStream != null)
                {
                    using (StreamReader responseReader = new StreamReader(webStream))
                    {
                        string response = responseReader.ReadToEnd();

                        JObject jo = JObject.Parse(response);

                        //success?
                        string path = jo.First.Path;
                        string result = jo.First.First.Value<string>();


                        if (path != "ok" || result != "True")
                        {
                            Console.WriteLine("Error recieved sending message!");
                            //throw new WebException("Failure code from web service");

                        }
                    }
                }
            }

        }
    }
}