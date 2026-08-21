
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
        public static BotOptions Options;
        public static Persistence.SqliteStateStore Store;

        /// <summary>
        /// -plugin's module allow-list is the one CLI flag kept as-is (unrelated to instance/
        /// credentials, low blast radius - see Core/Plugins.cs's initPluginAssemblies). -context is
        /// gone; ROBOTO_INSTANCE/ROBOTO_DATADIR env vars pick the instance now (see BotOptions/
        /// InstanceBootstrapper), matching the abandoned rewrite branch's own instance-identity
        /// design rather than legacy's per-context XML filename scheme.
        /// </summary>
        public static List<string> pluginFilter = new List<string>();

        private enum argtype { def, plugin };

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
                            case "-plugin":
                                mode = argtype.plugin;
                                break;
                        }
                        break;

                    case argtype.plugin:
                        pluginFilter.Add(arg);
                        mode = argtype.def;
                        break;


                }
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
            logging.longOp lo_s = new logging.longOp("Core Startup", 6);

            //Resolve which instance we are (ROBOTO_INSTANCE, default "default"), where its data
            //lives (ROBOTO_DATADIR, default /data), and its credentials ({DataDir}/{Instance}/
            //bot.env) - replaces -context + %appdata%\Roboto\<context>.xml entirely.
            var instance = Environment.GetEnvironmentVariable("ROBOTO_INSTANCE") ?? "default";
            var dataDir = Environment.GetEnvironmentVariable("ROBOTO_DATADIR") ?? "/data";
            log.setWindowTitle(instance);

            if (!InstanceBootstrapper.TryLoad(dataDir, instance, out var telegramToken, out var botUsername, out var steamApiKey, out var bootstrapMessage))
            {
                log.log(bootstrapMessage, logging.loglevel.critical, false, true);
                return;
            }

            Options = new BotOptions
            {
                Instance = instance,
                DataDir = dataDir,
                TelegramToken = telegramToken,
                BotUsername = botUsername,
                SteamApiKey = steamApiKey,
            };
            lo_s.addone();

            log.log("Opening database", logging.loglevel.high);
            Store = new Persistence.SqliteStateStore(System.IO.Path.Combine(Options.InstanceDir, "roboto.db"));
            Store.Initialize();
            lo_s.addone();

            //Load plugins before settings so that we know which module types to look for.
            log.log("Loading Plugins", logging.loglevel.high);
            Plugins.initPluginAssemblies();
            lo_s.addone();

            log.log("Loading Settings & data from disk", logging.loglevel.high);
            Settings = settings.load();
            if (Settings == null) {
                log.log("Failed to load settings - aborting.", logging.loglevel.critical);
                return;
            } //unable to load - abort.

            lo_s.addone();

            log.log("Loading Log", logging.loglevel.high);
            log.load();
            lo_s.complete();


            log.log("I am " + Settings.botUserName, logging.loglevel.critical, false, true);

            //setup TLS 1.2 - still needed for cardCast.cs/mod_steam_steamapi.cs's own hand-rolled
            //HttpWebRequest/WebClient calls (untouched, out of scope for this phase); Telegram.Bot's
            //own HttpClient usage doesn't need this.
            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            Settings.stats.startup();
            Plugins.startupChecks();

            Settings.save();

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