
using System.Text;
using System.Windows.Media;
using System.Text.RegularExpressions;
using System;
using System.Threading;
using System.Threading.Tasks;
//using System.Web;
using System.Net;
using System.IO;
//using System.Windows.Forms;
using System.Windows;
using System.Runtime.Serialization;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RobotoChatBot
{
    public class Roboto
    {
        public static LogWindow logWindow;
        private static Thread bgthread;
        public static DateTime startTime = DateTime.Now;
        
        public static settings Settings;
        public static logging log = new logging();
        /// <summary>
        /// This is the name of the instance that we are running - and the name of the XML file we save
        /// </summary>
        public static string context = null;
        public static List<string> pluginFilter = new List<string>();
        //public static bool quickStart = false;
        
        private enum argtype {def, context, plugin };
        
        [STAThread]
        static void Main(string[] args)
        {
            logWindow = new LogWindow();
            logWindow.Show();

            log.log("ROBOTO", logging.loglevel.critical,  Colors.White, false, true);
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
                            /*case "-quickstart":
                                quickStart = true;
                                break;*/
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
                log.setWindowTitle(Roboto.context);
                log.log( context + " context", logging.loglevel.high, Colors.White,false,true,false,true);
            }

           // Console.CancelKeyPress += new ConsoleCancelEventHandler(closeHandler);

            bgthread = new Thread(new ThreadStart(startBackground));
            bgthread.Start();

            //UI Thread cludge to enable it to run properly. Wasnt exiting cleanly from the UI thread before
            logWindow.Hide();

            logWindow.ShowDialog();

        }

      
        public static void shudownMainThread()
        {
            log.log("Close Signal Recieved in main thread", logging.loglevel.high, Colors.White, false, true);
            if (Settings != null)
            {
                log.log("This could take up to " + Settings.waitDuration + " seconds to complete");
            }

            Messaging.quit();
        }

        private static void startBackground()
        {
            logging.longOp lo_s = new logging.longOp("Core Startup", 5);

            //Load plugins before XML so that we have datatypes etc.. to play with
            log.log("Loading Plugins", logging.loglevel.high);
            Plugins.initPluginAssemblies();
            lo_s.addone();

            //Now load XML so that we have datatypes etc.. to play with
            log.log("Loading Settings & data from disk", logging.loglevel.high);
            Settings = settings.load();
            if (Settings == null) {
                logWindow.unlockExit();
                log.log("Failed to load settings file - aborting. Please close the window", logging.loglevel.critical);
                return;
            } //unable to load - abort. 

            lo_s.addone();

            log.log("Loading Log", logging.loglevel.high);
            log.load();
            lo_s.complete();


            log.log("I am " + Settings.botUserName, logging.loglevel.critical, Colors.White, false, true);

            //setup TLS 1.2
            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            //only use for debug: 
            //if (!quickStart)
            //{
            Settings.stats.startup();
            Plugins.startupChecks();
            //}


            /*
             * 
             * TRIGGER THINGS IN DEBUG - TEMPORARY CODE, REMOVE
             *             
            #warning REMOVE THIS FUCKING CODE
            while (1 == 1)
            {
                Settings.backgroundProcessing(true);
            }
             */


            //AT THIS POINT THE GAME WILL START PROCESSING INSTRUCTIONS!!!
            //DONT GO PAST IN STARTUP TEST MODE
            //----------------------------
            int ABANDONALLHOPE = 1;
            ABANDONALLHOPE++;
            //----------------------------
            
            Settings.save();

            if (Settings.isFirstTimeInitialised)
            {
                log.log(@"New XML file created in %appdata%\Roboto\ . Enter your API key in there and restart.", logging.loglevel.critical, Colors.White, false, true);
            }
            else
            {
                log.log("Starting main thread", logging.loglevel.high);


                Messaging.processUpdates();

                //Perform all background processing, syncing etc..
                Plugins.backgroundProcessing(false);
                

                log.log("Main loop finishing, saving" , logging.loglevel.high);
                Roboto.Settings.save();
                log.log("Saved data, exiting main loop", logging.loglevel.high);
                //todo - do something to allow window to close? 
                logWindow.unlockExit();
                log.log("All data saved cleanly - close the form again to exit", logging.loglevel.critical);
            }
       

        }
        
    }
}