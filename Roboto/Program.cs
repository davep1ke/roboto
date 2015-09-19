
using System.Text;
using System;
using System.Threading;
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
            Console.CancelKeyPress += new ConsoleCancelEventHandler(closeHandler);
            settings.loadPlugins();
            Settings = settings.load();
            Settings.validate();
            Roboto.Process();
            Settings.save();

        }

        /// <summary>
        /// Save the settings when Ctrl-C'd. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        protected static void closeHandler(object sender, ConsoleCancelEventArgs args)
        {
            Console.WriteLine("Sending Close Signal.");
            if (Settings != null)
            {
                Console.WriteLine("This could take up to " + Settings.waitDuration + " seconds to complete");
            }
            endLoop = true;
            args.Cancel = true; //prevent actual close. Wait for loop to exit.
        }

        //Commands
        /*
          
BotFather:

/setcommands
         
craft - Wordcraft something
craft_add - Adds a word to the craft dictionary (prompts)
craft_remove - Removes a word from the craft dictionary (prompts)
quote - Says a quote
addquote - Adds a quote to the quote DB (prompts)
birthday - Add a birthday to the database (will be announced on the day)
reminder - Adds a reminder that will be announced on the day
save - Saves anything that hasnt been saved to disk yet 
          
         */


        /// <summary>
        /// Main Roboto process. Connects to Telegram and processes any updates. 
        /// </summary>
        private static void Process()
        {
            DateTime lastUpdate = DateTime.MinValue;


            while (!endLoop)
            {
                //store the time to prevent hammering the service when its down
                if (lastUpdate > DateTime.Now.Subtract(TimeSpan.FromSeconds(10)))
                {
                    Console.WriteLine("Too quick, sleeping");
                    Thread.Sleep(10000);
                }
                lastUpdate = DateTime.Now;

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
                                    throw new WebException("Failure code from web service");

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
                                        
                                        Settings.lastUpdate = updateID;

                                        //Do we have an incoming message?
                                        if (update_TK.Path == "result[0].message" && update_TK.SelectToken(".text") != null)
                                        {
                                            //prevent delays - its sent something valid back to us so we are probably OK. 
                                            lastUpdate = DateTime.MinValue;

                                            message m = new message(update_TK);

                                            #region actions
                                            //now decide what to do with this stuff.
                                            bool processed = false;
                                            //TODO - do this in priority order :(
                                            foreach (Modules.RobotoModuleTemplate plugin in settings.plugins)
                                            {
                                                if (plugin.chatHook && (!processed  || plugin.chatEvenIfAlreadyMatched))
                                                {
                                                    plugin.chatEvent(m);

                                                }
                                            }


                                            #region save
                                            if (m.text_msg.StartsWith("/save"))
                                            {
                                                Settings.save();
                                                TelegramAPI.SendMessage(m.chatID, "Saved settings");
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

                //TODO - process background actions

                foreach (Modules.RobotoModuleTemplate plugin in settings.plugins)
                {
                    if (plugin.backgroundHook)
                    {
                        plugin.callBackgroundProcessing();
                    }
                }
                
            }
        }

        
    }
}