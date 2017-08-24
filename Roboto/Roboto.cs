
using System.Text;
using System.Text.RegularExpressions;
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
            log.initialise();
            log.log("I am " + Settings.botUserName, logging.loglevel.critical, ConsoleColor.White, false, true);
            Settings.startupChecks();

            //test

           
            if (!Settings.isFirstTimeInitialised)
            {
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
            log.finalise();
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
                //store the time to prevent hammering the service when its down. Pause for a couple of seconds if things are getting toasty
                if (lastUpdate > DateTime.Now.Subtract(TimeSpan.FromSeconds(10)))
                {
                    Roboto.Settings.stats.logStat(new statItem("Hammering Prevention", typeof(Roboto)));
                    log.log("Too quick, sleeping", logging.loglevel.warn );
                    Thread.Sleep(2000);
                }
                lastUpdate = DateTime.Now;

                //TODO - move this code to the webADI class
                string updateURL = Settings.telegramAPIURL + Settings.telegramAPIKey + "/getUpdates" +
                    "?offset=" + Settings.getUpdateID() +
                    "&timeout=" + Settings.waitDuration +
                    "&limit=10";

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(updateURL);
                log.log(".", logging.loglevel.low, ConsoleColor.White, true );
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
                                    log.log("Failure code from web service", logging.loglevel.high);
                                    throw new WebException("Failure code from web service");

                                }
                                else
                                {
                                    int resultID = 0;
                                    //open the response and parse it using JSON. Probably only one result, but should be more? 
                                    foreach (JToken token in jo.SelectTokens("result.[*]"))//jo.Children()) //) records[*].data.importedPath"
                                    {
                                        string logText = Regex.Replace(token.ToString(), @"(\s|\n|\r|\r\n)+", " ");
                                        log.log(logText, logging.loglevel.verbose);
                                        
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
                                            string chatTitle = update_TK.SelectToken("chat.title").Value<string>();
                                            //new chat, add
                                            if (chatData == null)
                                            {
                                                chatData = Settings.addChat(chatID, chatTitle);
                                            }
                                            if (chatData == null)
                                            {
                                                throw new DataMisalignedException("Something went wrong creating the new chat data");
                                            }
                                            chatData.setTitle(chatTitle);
                                        }
                                       


                                        //Do we have an incoming message?
                                        if (update_TK.Path == "result[" + resultID.ToString() + "].message" && update_TK.SelectToken(".text") != null)
                                        {
                                            //prevent delays - its sent something valid back to us so we are probably OK. 
                                            lastUpdate = DateTime.MinValue;
                                            if (chatData != null) { chatData.resetLastUpdateTime(); }

                                            message m = new message(update_TK);

                                            //now decide what to do with this stuff.
                                            bool processed = false;

                                            //check if this is an expected reply, and if so route it to the 
                                            Settings.parseExpectedReplies(m);
                                            

                                            //TODO - call plugins in some kind of priority order
                                            foreach (Modules.RobotoModuleTemplate plugin in settings.plugins)
                                            {
                                                

                                                //Skip this message if the chat is muted. 
                                                if (plugin.chatHook && (chatData == null || (chatData.muted == false || plugin.chatIfMuted)))
                                                {
                                                    if ((!processed || plugin.chatEvenIfAlreadyMatched))
                                                    {
                                                        processed = plugin.chatEvent(m, chatData);
                                                        
                                                    }
                                                }
                                            }
                                            
                                        }
                                        else
                                        {
                                            log.log("No text in update", logging.loglevel.verbose);
                                        }
                                        //dont know what other update types we want to monitor? 
                                        //TODO - leave / kicked / chat deleted
                                        resultID++;
                                    }

                                    //housekeeping
                                    //check that all players have been sent a message, if there is one in the stack. This is to double check that if e.g. a game is cancelled the player doesnt get stuck
                                    Settings.expectedReplyHousekeeping();
                                }
                            }
                        }
                    }
                }
                catch (System.Net.WebException e)
                {
                    log.log("Web Service Timeout during getUpdates: " + e.ToString(), logging.loglevel.high);
                    Settings.stats.logStat(new statItem("BotAPI Timeouts", typeof(Roboto)));
                }

                catch (Exception e)
                {
                    try
                    {
                        log.log("Exception caught at main loop. " + e.ToString(), logging.loglevel.critical, ConsoleColor.White, false, false, false, false, 2);
                    }

                    catch (Exception ex)
                    {
                        Console.Out.WriteLine("-----------------");
                        Console.Out.WriteLine("Error During LOGGING! Original Error was");
                        Console.Out.WriteLine(e.Message);
                        Console.Out.WriteLine("Logging Error was");
                        Console.Out.WriteLine(ex.Message);

                    }
                }

                Settings.backgroundProcessing(false);
                
               
                
            }
        }

        
    }
}