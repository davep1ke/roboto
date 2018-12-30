using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
//using System.Drawing;
using System.Windows.Media;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows;
using System.Threading;
using System.Windows.Threading;
using System.ComponentModel;
using System.Data;



namespace RobotoChatBot
{
    /// <summary>
    /// Class for logging data in a standard format. 
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
            public Color? colour { get; set; } = null;
            public bool noLineBreak { get; set; } = false;
            public bool banner { get; set; } = false;
            public bool pause { get; set; } = false;
            public bool skipheader { get; set; } = false;
            public int skipLevel { get; set; } = 1;
            public string classtype { get; set; }
            public string methodName { get; set; }

            public logItem(string text, loglevel level = loglevel.normal, Color? colour = null, bool noLineBreak = false, bool banner = false, bool pause = false, bool skipheader = false, int skipLevel = 1)
            {
                this.logText = text;
                this.level = level;
                this.colour = colour;
                this.noLineBreak = noLineBreak;
                this.banner = banner;
                this.pause = pause;
                this.skipheader = skipheader;
                this.skipLevel = skipLevel;

                StackFrame frame = new StackFrame(skipLevel);
                var method = frame.GetMethod();
                classtype = method.DeclaringType.ToString();
                methodName = method.Name;
            }

            public override string ToString()
            {

                //add our time and module stamps
                string outputString = "";
                if (banner == false && skipheader == false)
                {
                    outputString += DateTime.Now.ToString("dd-MM-yyyy  HH:mm:ss") + " - "
                        + level.ToString().Substring(0, 2).ToUpper() + " - "
                        + (classtype.ToString() + ":" + methodName).PadRight(45)
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

            internal Color getColor()
            {
                if (colour != null) { return (Color)colour; }


                switch (level)
                {
                    case loglevel.verbose:
                        return Colors.Gray;
                    case loglevel.low:
                        return Colors.DarkGreen;
                    case loglevel.normal:
                        return Colors.Cyan;
                    case loglevel.warn:
                        return Colors.Magenta;
                    case loglevel.high:
                        return Colors.Yellow;
                    case loglevel.critical:
                        return Colors.Red;
                }

                return Colors.White;
            }
        }

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
                Roboto.logWindow.addOrUpdateLongOp(this);
            }

            public longOp(string name, int totalLength, longOp parent)
            {
                this.name = name;
                this.totalLength = totalLength;
                this.parent = parent;
                Roboto.log.registerLongOp(this);
                Roboto.logWindow.addOrUpdateLongOp(this);
            }

            public void updateLongOp(int current, bool complete = false)
            {
                this.currentPos = current;
                Roboto.logWindow.addOrUpdateLongOp(this);

                if (complete) { this.complete(); }
            }

            public void complete()
            {
                Roboto.log.unregisterLongOp(this);
                Roboto.logWindow.removeProgressBar(this);
            }

            public void addone()
            {
                updateLongOp(currentPos+1);
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
        private Char bannerChar = "*".ToCharArray()[0];
        private DateTime currentLogFileDate = DateTime.MinValue;
        private DateTime logLastFlushed = DateTime.Now;
        private static string windowTitleCore = "Roboto ChatBot";
        private string windowTitle = windowTitleCore;
        private List<longOp> longOps = new List<longOp>();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="text"></param>
        /// <param name="level"></param>
        /// <param name="colour"></param>
        /// <param name="noLineBreak"></param>
        /// <param name="banner"></param>
        /// <param name="pause"></param>
        /// <param name="skipheader"></param>
        /// <param name="skipLevel">Levels of the stack to skip when getting the calling class</param>
        public void log(string text, loglevel level = loglevel.normal, Color? colour = null, bool noLineBreak = false, bool banner = false, bool pause = false, bool skipheader = false, int skipLevel = 1)
        {
            log(new logItem(text, level, colour, noLineBreak, banner, pause, skipheader, skipLevel));
        }

        /// <summary>
        /// Add an item to the log
        /// </summary>
        /// <param name="thisLogItem"></param>
        public void log (logItem thisLogItem)
        { 
            //check logfile correct
            if (initialised && Roboto.Settings.enableFileLogging && DateTime.Now > currentLogFileDate.AddHours(Roboto.Settings.rotateLogsEveryXHours))
            {
                initialised = false;
                try
                {
                    log("Rotating Logs", loglevel.warn, Colors.White, false, true);
                    finalise();
                    initialise();
                }
                catch (Exception e)
                {
                    initialised = false;
                    log("Error rotating logs! File logging disabled. " + e.ToString(), loglevel.critical);
                }
            }

            if (logLastFlushed < DateTime.Now.AddMinutes(-5) )
            {
                textWriter.Flush();
                logLastFlushed = DateTime.Now;
                log("Flushed logfile", loglevel.low);
                
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

             
                //TODO - pause
                /*if (pause)
                {
                    writeLine(new logItem("Press any key to continue", loglevel.critical, ConsoleColor.Red,false,false,true);
                    Console.ReadKey();
                }*/

            }
            Console.ResetColor();
        }

        public void setWindowTitle(string title)
        {
            this.windowTitle = windowTitleCore + " " + title;
            if (Roboto.logWindow != null) { Roboto.logWindow.setTitle(windowTitleCore + " " + title); }
        }

        public string getWindowTitle()
        {
            return windowTitle;
        }

        internal void finalise()
        {
            log("Closing logfile", loglevel.warn );
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
            //Console.WriteLine(s);
            Roboto.logWindow.addLogItem(thisLogItem);
            

            if (initialised && textWriter != null)
            {
                if (thisLogItem.banner == true) { textWriter.WriteLine("************************"); }
                textWriter.WriteLine(thisLogItem.logText);
                if (thisLogItem.banner == true) { textWriter.WriteLine("************************"); }
            }
        }

        private void write(string s)
        {
            //cleanse our text of anything we shouldnt log
            s = cleanse(s);
            //append the text to the last item.
            Roboto.logWindow.appendText(s);


            //logitems[logitems.Count - 1].logText += s;

            //Console.Write(s);
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


        public void initialise()
        {
            //Set up any stats
            Roboto.Settings.stats.registerStatType("Critical Errors", typeof(logging), System.Drawing.Color.Crimson, stats.displaymode.bar);
            Roboto.Settings.stats.registerStatType("High Errors", typeof(logging), System.Drawing.Color.Orange, stats.displaymode.bar);
            
            //todo - remove any logs older than x days.

            if (Roboto.Settings.enableFileLogging)
            {
                //Setup our logging
                currentLogFileDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, 0, 0);
                string logfile = settings.foldername + Roboto.Settings.botUserName + " " + DateTime.Now.ToString("yyyy-MM-dd HH") + ".log";
                textWriter = new StreamWriter(logfile, true, new UTF8Encoding(),65536);
                for (int i = 0; i < 10; i++) { textWriter.WriteLine(); }
                initialised = true;
                log("Enabled logging to file " + logfile, loglevel.warn);

            }
            else
            {
                log("File logging is disabled. Enable in the xml configuration file." );
            }

            

        }


    }
}
