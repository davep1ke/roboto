using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Serilog;
using Serilog.Events;

namespace RobotoChatBot
{
    /// <summary>
    /// Class for logging data in a standard format.
    ///
    /// Ported off WPF: this used to write to both a file and a WPF LogWindow, with every log() call
    /// carrying a System.Windows.Media.Color? for the LogWindow's text color - neither WPF concept
    /// survives a headless Linux/Docker port. Kept the exact public method shapes (log/logItem/
    /// longOp/loglevel/setWindowTitle/cleanse) so the hundreds of existing call sites across every
    /// module didn't need to change, minus the colour parameter itself (mechanically stripped from
    /// every call site - it only ever drove LogWindow text color, nothing else). Output now goes
    /// through Serilog (console sink) plus, when enableFileLogging is set, a rotating plain-text
    /// log file (see load()/write() below) - a DB sink was added in an earlier phase and then
    /// removed (see the constructor's own comment for why).
    /// </summary>
    public class logging
    {
        /// <summary>
        /// A single entry
        /// </summary>
        public class logItem
        {
            public string logText { get; set; }
            public loglevel level { get; set; } = loglevel.normal;
            public bool noLineBreak { get; set; } = false;
            public bool banner { get; set; } = false;
            public bool pause { get; set; } = false;
            public bool skipheader { get; set; } = false;
            public int skipLevel { get; set; } = 1;
            public string classtype { get; set; }
            public string methodName { get; set; }

            public logItem(string text, loglevel level = loglevel.normal, bool noLineBreak = false, bool banner = false, bool pause = false, bool skipheader = false, int skipLevel = 2)
            {
                this.logText = text;
                this.level = level;
                this.noLineBreak = noLineBreak;
                this.banner = banner;
                this.pause = pause;
                this.skipheader = skipheader;
                this.skipLevel = skipLevel;

                try
                {
                    StackFrame frame = new StackFrame(skipLevel);
                    if (frame == null) { throw new SystemException("Frame Not Found"); }
                    var method = frame.GetMethod();
                    if (method == null)
                    {
                        //not sure why this is an issue. For early calls on the VM this throws an exception so hardcode it if we can't find a method for the frame.
                        methodName = "Root";
                        classtype = "Class";
                        return;
                    }
                    methodName = method.Name;
                    if (method.DeclaringType == null) { throw new SystemException("Method Declaring Type not found"); }
                    classtype = method.DeclaringType.ToString();
                    if (classtype == null) { throw new SystemException("Method Not Found"); }

                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("Couldnt create logItem.\r\n" + e.ToString());
                    Roboto.log.log("Couldnt create logItem.\r\n" + e.ToString(), loglevel.critical);
                }
            }

            public override string ToString()
            {

                //add our time and module stamps
                string outputString = "";
                if (banner == false && skipheader == false)
                {
                    outputString += DateTime.Now.ToString("dd-MM-yyyy  HH:mm:ss") + " - "
                        + level.ToString().Substring(0, 2).ToUpper() + " - "
                        + (classtype.ToString().Replace("RobotoChatBot.", "") + ":" + methodName).PadRight(45)
                        + " - ";
                }
                else
                {
                    outputString += "".PadRight(53);
                }

                //add the main text
                outputString += logText;
                return outputString;
            }

            internal LogEventLevel getSerilogLevel()
            {
                switch (level)
                {
                    case loglevel.verbose:
                        return LogEventLevel.Verbose;
                    case loglevel.low:
                        return LogEventLevel.Debug;
                    case loglevel.normal:
                        return LogEventLevel.Information;
                    case loglevel.warn:
                        return LogEventLevel.Warning;
                    case loglevel.high:
                        return LogEventLevel.Error;
                    case loglevel.critical:
                        return LogEventLevel.Fatal;
                }

                return LogEventLevel.Information;
            }
        }

        // longOp (a WPF progress-bar abstraction - Roboto.logWindow.addOrUpdateLongOp/
        // removeProgressBar) was removed entirely, not just stubbed: it was pure UI plumbing (a
        // List<longOp> registered/unregistered per operation but never read back anywhere, plus a
        // .Parent/.CurrentPos never read either) that, post-port, only added log volume (a "starting"
        // line, a verbose line per step, a "complete" line) with nothing left to consume it. Every
        // former call site now times itself with a Stopwatch and records duration (plus, where the
        // step count was a real variable business quantity rather than a fixed phase counter, an item
        // count) via Roboto.Settings.stats.logStat instead - see e.g. mod_xyzzy.startupChecks()'s
        // "... Duration (ms)" stat registrations.

        public enum loglevel { verbose, low, normal, warn, high, critical }
        private StreamWriter textWriter = null;
        private bool initialised = false;
        private bool followOnLine = false;
        private DateTime currentLogFileDate = DateTime.MinValue;
        private DateTime logLastFlushed = DateTime.Now;
        private static string windowTitleCore = "Roboto ChatBot";
        private string windowTitle = windowTitleCore;
        private ILogger serilog;

        public logging()
        {
            // A DB sink (writing every log line to a `logs` table) was added here in an earlier
            // phase, then removed: nothing in the codebase ever read the table back out, and it
            // meant every single log() call opened a fresh SqliteConnection and did a synchronous
            // INSERT+commit - measured at ~30-80ms per call, which dominated the cost of any
            // startup path that logs per-item/per-chat (e.g. mod_xyzzy_coredata.startupChecks).
            // enableFileLogging's rotating text log (below) already gives the same "survives a
            // crash" durability the DB sink existed for. See Persistence/DataFixes.cs's
            // "0001_drop_logs_table" for the corresponding schema cleanup.
            serilog = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }

        /// <param name="text"></param>
        /// <param name="level"></param>
        /// <param name="noLineBreak"></param>
        /// <param name="banner"></param>
        /// <param name="pause"></param>
        /// <param name="skipheader"></param>
        /// <param name="skipLevel">Levels of the stack to skip when getting the calling class</param>
        public void log(string text, loglevel level = loglevel.normal, bool noLineBreak = false, bool banner = false, bool pause = false, bool skipheader = false, int skipLevel = 2)
        {
            log(new logItem(text, level, noLineBreak, banner, pause, skipheader, skipLevel));
        }

        /// <summary>
        /// Add an item to the log
        /// </summary>
        /// <param name="thisLogItem"></param>
        public void log(logItem thisLogItem)
        {
            //check logfile correct
            if (initialised && Roboto.Settings.enableFileLogging && DateTime.Now > currentLogFileDate.AddHours(Roboto.Settings.rotateLogsEveryXHours))
            {
                initialised = false;
                try
                {
                    log("Rotating Logs", loglevel.warn, false, true);
                    finalise();
                    load();
                }
                catch (Exception e)
                {
                    initialised = false;
                    log("Error rotating logs! File logging disabled. " + e.ToString(), loglevel.critical);
                }
            }

            if (logLastFlushed < DateTime.Now.AddMinutes(-5))
            {
                try
                {
                    textWriter.Flush();
                    logLastFlushed = DateTime.Now;
                    log("Flushed logfile", loglevel.low);
                }
                catch (Exception e)
                {
                    log("Failed to flush log. " + e.ToString(), loglevel.critical);
                }
            }


            if (initialised && thisLogItem.level == loglevel.high)
            {
                Roboto.Settings.stats.logStat(new statItem("High Errors", typeof(logging)));
            }

            if (initialised && thisLogItem.level == loglevel.critical)
            {
                Roboto.Settings.stats.logStat(new statItem("Critical Errors", typeof(logging)));
            }


            if (thisLogItem.noLineBreak)
            {
                write(thisLogItem.logText);
                followOnLine = true;
            }
            else
            {
                //clear any trailing lines from write's instead of writelines
                if (followOnLine)
                {
                    writeLine();
                    followOnLine = false;
                }


                //write the main line
                writeLine(thisLogItem);
            }
        }

        public void setWindowTitle(string title)
        {
            this.windowTitle = windowTitleCore + " " + title;
        }

        public string getWindowTitle()
        {
            return windowTitle;
        }

        internal void finalise()
        {
            log("Closing logfile", loglevel.warn);
            textWriter.Flush();
            textWriter.Close();
            initialised = false;
        }

        /// <summary>
        /// generally for flushing a half written line
        /// </summary>
        private void writeLine()
        {
            writeLine(new logItem(""));
        }

        private void writeLine(logItem thisLogItem)
        {
            thisLogItem.logText = cleanse(thisLogItem.logText);
            serilog.Write(thisLogItem.getSerilogLevel(), thisLogItem.ToString());

            if (initialised && textWriter != null)
            {
                if (thisLogItem.banner == true) { textWriter.WriteLine("************************"); }
                textWriter.WriteLine(thisLogItem.ToString()); //logtext
                if (thisLogItem.banner == true) { textWriter.WriteLine("************************"); }
            }
        }

        private void write(string s)
        {
            //cleanse our text of anything we shouldnt log
            s = cleanse(s);
            serilog.Write(LogEventLevel.Information, s);

            if (Roboto.Settings.enableFileLogging && textWriter != null)
            {
                textWriter.Write(s);
            }
        }

        /// <summary>
        /// Remove the API key from any outbound messages
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        private string cleanse(string s)
        {
            if (Roboto.Settings != null && Roboto.Settings.telegramAPIKey != null)
            {
                s = s.Replace(Roboto.Settings.telegramAPIKey, "<APIKEY>");
            }
            return s;
        }


        public void load()
        {
            //Set up any stats
            Roboto.Settings.stats.registerStatType("Critical Errors", typeof(logging), System.Drawing.Color.Crimson, stats.displaymode.bar);
            Roboto.Settings.stats.registerStatType("High Errors", typeof(logging), System.Drawing.Color.Orange, stats.displaymode.bar);

            //todo - remove any logs older than x days.

            if (Roboto.Settings.enableFileLogging)
            {
                //Setup our logging - was settings.foldername + "\Roboto\" (a literal backslash, not
                //a real path separator on Linux - harmless-but-wrong, produced flat oddly-named
                //files instead of a real subdirectory tree, see MIGRATION.md's phase 0 notes).
                //Options.InstanceDir + Path.Combine fixes it as a natural side effect of moving to
                //the per-instance folder scheme.
                currentLogFileDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, 0, 0);
                if (!Directory.Exists(Roboto.Options.InstanceDir)) { Directory.CreateDirectory(Roboto.Options.InstanceDir); }
                string logfile = Path.Combine(Roboto.Options.InstanceDir, Roboto.Settings.botUserName + " " + DateTime.Now.ToString("yyyy-MM-dd HH") + ".log");
                textWriter = new StreamWriter(logfile, true, new UTF8Encoding(), 65536);
                for (int i = 0; i < 10; i++) { textWriter.WriteLine(); }
                initialised = true;
                log("Enabled logging to file " + logfile, loglevel.warn);

            }
            else
            {
                log("File logging is disabled. Enable in the xml configuration file.");
            }



        }


    }
}
