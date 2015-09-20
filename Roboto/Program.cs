
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
                                        Console.WriteLine(token.ToString());
                                        

                                        //Find out what kind of message this is.

                                        //TOP LEVEL TOKENS
                                        JToken updateID_TK = token.First;
                                        JToken update_TK = updateID_TK.Next.First;
                                        

                                        //is this for a group chat?
                                        
                                        int chatID = update_TK.SelectToken("chat.id").Value<int>();
                                        chat chatData = null;
                                        if (chatID < 0)
                                        {
                                            //find the chat 
                                            chatData = Settings.getChat(chatID);
                                            //new chat, add
                                            if (chatData == null)
                                            {
                                                chatData = Settings.addChat(chatID);
                                            }
                                            if (chatData == null)
                                            {
                                                throw new DataMisalignedException("Something went wrong creating the new chat data");
                                            }
                                        }


                                        //Do we have an incoming message?
                                        if (update_TK.Path == "result[0].message" && update_TK.SelectToken(".text") != null)
                                        {
                                            //prevent delays - its sent something valid back to us so we are probably OK. 
                                            lastUpdate = DateTime.MinValue;

                                            message m = new message(update_TK);

                                            //now decide what to do with this stuff.
                                            bool processed = false;
                                            //TODO - do this in priority order :(
                                            foreach (Modules.RobotoModuleTemplate plugin in settings.plugins)
                                            {
                                                if (plugin.chatHook && (!processed  || plugin.chatEvenIfAlreadyMatched))
                                                {
                                                    plugin.chatEvent(m, chatData);

                                                }
                                            }

                                        }
                                        //dont know what other update types we want to monitor? 



                                        //Flag the update ID as processed.
                                        int updateID = updateID_TK.First.Value<int>();
                                        Settings.lastUpdate = updateID;
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