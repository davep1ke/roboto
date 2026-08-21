
using System.Text;
using System.Text.RegularExpressions;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.IO;
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
        public static DateTime startTime = DateTime.Now;

        public static settings Settings;
        public static logging log = new logging();
        /// <summary>
        /// This is the name of the instance that we are running - and the name of the XML file we save
        /// </summary>
        public static string context = null;
        public static List<string> pluginFilter = new List<string>();

        private enum argtype {def, context, plugin };

        /// <summary>
        /// Was [STAThread] with a WPF LogWindow shown via ShowDialog() on the UI thread, with all
        /// real work handed off to a separate "bgthread" so the UI thread stayed responsive - no UI
        /// any more, so that split (and the STAThread requirement it existed for) is gone. Runs
        /// startBackground() directly on the main thread now.
        /// </summary>
        static void Main(string[] args)
        {
            log.log("ROBOTO", logging.loglevel.critical, false, true);
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
                log.setWindowTitle(Roboto.context);
                log.log( context + " context", logging.loglevel.high, false, true);
            }

            startBackground();
        }


        public static void shudownMainThread()
        {
            log.log("Close Signal Recieved in main thread", logging.loglevel.high, false, true);
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
                log.log("Failed to load settings file - aborting.", logging.loglevel.critical);
                return;
            } //unable to load - abort.

            lo_s.addone();

            log.log("Loading Log", logging.loglevel.high);
            log.load();
            lo_s.complete();


            log.log("I am " + Settings.botUserName, logging.loglevel.critical, false, true);

            //setup TLS 1.2
            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            Settings.stats.startup();
            Plugins.startupChecks();

            Settings.save();

            if (Settings.isFirstTimeInitialised)
            {
                log.log(@"New settings created - enter your API key in the config and restart.", logging.loglevel.critical, false, true);
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
            }


        }

    }
}