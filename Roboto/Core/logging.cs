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
    /// through Serilog (console sink here; a DB sink with a 30-day purge is added in a later phase -
    /// see MIGRATION.md).
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

        /// <summary>
        /// Was a WPF progress-bar abstraction (Roboto.logWindow.addOrUpdateLongOp/removeProgressBar) -
        /// no UI to drive any more, so this now just logs its own start/step/completion as plain log
        /// lines instead of rendering a progress bar. Kept as a class (not deleted outright) since
        /// every call site (Plugins.cs, mod_xyzzy.cs, Roboto.cs startup) constructs one positionally
        /// and calls addone()/complete() - changing those call sites isn't needed for this to work.
        /// </summary>
        public class longOp
        {
            public string name;
            public int totalLength;
            private int currentPos = 0;

            private longOp parent;

            public int CurrentPos
            {
                get
                {
                    return currentPos;
                }
            }

            public longOp Parent
            {
                get
                {
                    return parent;
                }

            }

            public longOp(string name, int totalLength)
            {
                this.name = name;
                this.totalLength = totalLength;
                Roboto.log.registerLongOp(this);
                Roboto.log.log($"{name}: starting ({totalLength} steps)", loglevel.low);
            }

            public longOp(string name, int totalLength, longOp parent)
            {
                this.name = name;
                this.totalLength = totalLength;
                this.parent = parent;
                Roboto.log.registerLongOp(this);
                Roboto.log.log($"{name}: starting ({totalLength} steps)", loglevel.low);
            }

            public void updateLongOp(int current, bool complete = false)
            {
                this.currentPos = current;
                Roboto.log.log($"{name}: {current}/{totalLength}", loglevel.verbose);

                if (complete) { this.complete(); }
            }

            public void complete()
            {
                Roboto.log.unregisterLongOp(this);
                Roboto.log.log($"{name}: complete", loglevel.low);
            }

            public void addone()
            {
                updateLongOp(currentPos + 1);
            }
        }

        protected void unregisterLongOp(longOp longOp)
        {
            longOps.Remove(longOp);
        }

        protected void registerLongOp(longOp longOp)
        {
            longOps.Add(longOp);
        }

        public enum loglevel { verbose, low, normal, warn, high, critical }
        private StreamWriter textWriter = null;
        private bool initialised = false;
        private bool followOnLine = false;
        private DateTime currentLogFileDate = DateTime.MinValue;
        private DateTime logLastFlushed = DateTime.Now;
        private static string windowTitleCore = "Roboto ChatBot";
        private string windowTitle = windowTitleCore;
        private List<longOp> longOps = new List<longOp>();
        private ILogger serilog;

        public logging()
        {
            //DbLogSink writes to Roboto.Store (the logs table) - see its own comment on why this is
            //additive to the console sink, not a replacement, and why it's safe to construct here
            //even though Roboto.Store doesn't exist yet at this point (this runs from Roboto.log's
            //static field initializer, before startBackground()'s instance bootstrap) - it just
            //no-ops until the store is actually available.
            serilog = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.Sink(new DbLogSink())
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
