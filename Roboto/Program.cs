
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
        public static DateTime startTime = DateTime.Now;
        private static bool endLoop = false;
        public static settings Settings;
        public static logging log = new logging();
        /// <summary>
        /// This is the name of the instance that we are running - and the name of the XML file we save
        /// </summary>
        public static string context = null;
        public static List<string> pluginFilter = new List<string>();

        private enum argtype {def, context, plugin };
        

        static void Main(string[] args)
        {
            log.log("ROBOTO", logging.loglevel.critical, ConsoleColor.White, false, true);
            log.log("Telegram Bot Startup", logging.loglevel.low);
         
            argtype mode = argtype.def;
             
            //parse arguments
            foreach(string arg in args)
            {
                switch (mode)
                {
                    case argtype.def:
                        switch (arg)
                        {
                            case "-context":
                                mode = argtype.context;
                                break;
                            case "-plugin":
                                mode = argtype.plugin;
                                break;
                        }
                        break;

                    case argtype.context:
                        context = arg;
                        mode = argtype.def;
                        break;

                    case argtype.plugin:
                        pluginFilter.Add(arg);
                        mode = argtype.def;
                        break;


                }
            }

            if (context != null)
            {
                log.setTitle(Roboto.context);
                log.log( context + " context", logging.loglevel.high,ConsoleColor.White,false,true,false,true);
            }

            Console.CancelKeyPress += new ConsoleCancelEventHandler(closeHandler);
            settings.loadPlugins();

            log.log( "Loading Settings", logging.loglevel.high);
            Settings = settings.load();
            Settings.validate();

            if (!Settings.isFirstTimeInitialised)
            {
                log.log("I am " + Settings.botUserName, logging.loglevel.critical, ConsoleColor.White, false, true);

                log.log( "Starting main thread", logging.loglevel.high);
                Roboto.Process();
            }
            else
            {
                log.log( @"New XML file created in %appdata%\Roboto\ . Enter your API key in there and restart.", logging.loglevel.critical, ConsoleColor.White, false, true);

                Settings.save();
            }

            log.log( "Saving & exiting", logging.loglevel.high);
            Settings.save();

            log.log( "Exiting", logging.loglevel.high, ConsoleColor.White, false, true, true);
        }

        /// <summary>
        /// Save the settings when Ctrl-C'd. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        protected static void closeHandler(object sender, ConsoleCancelEventArgs args)
        {
            log.log("Sending Close Signal.", logging.loglevel.high,ConsoleColor.White,false,true);
            if (Settings != null)
            {
                log.log( "This could take up to " + Settings.waitDuration + " seconds to complete");
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
                log.log(".", logging.loglevel.low,ConsoleColor.White, true );
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

                                        //Flag the update ID as processed.
                                        int updateID = updateID_TK.First.Value<int>();
                                        Settings.lastUpdate = updateID;

                                        //is this for a group chat?
                                        
                                        long chatID = update_TK.SelectToken("chat.id").Value<long>();
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

                                            //check if this is an expected reply, and if so route it to the 
                                            Settings.parseExpectedReplies(m);

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



                                       
                                    }

                                    //housekeeping
                                    //check that all players have been sent a message, if there is one in the stack. This is to double check that if e.g. a game is cancelled the player doesnt get stuck
                                    Settings.expectedReplyHousekeeping();

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
                
                foreach (Modules.RobotoModuleTemplate plugin in settings.plugins)
                {
                    if (plugin.backgroundHook)
                    {
                        try
                        {
                            plugin.callBackgroundProcessing();
                        }
                        catch (Exception e)
                        {
                            Console.Out.WriteLine("-----------------");
                            Console.Out.WriteLine("Error During Plugin " + plugin.GetType().ToString() + " background processing");
                            Console.Out.WriteLine(e.Message);
                        }
                    }
                }
                
            }
        }

        
    }
}