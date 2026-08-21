using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace RobotoChatBot
{

    /// <summary>
    /// inheritable. Should be created by the plugin.
    /// </summary>
    public class statType
    {
        public string name = "";
        public string moduleType = "";
        public stats.displaymode displayMode = stats.displaymode.line;
        public stats.statmode statMode = stats.statmode.increment;
        public List<statSlice> statSlices = new List<statSlice>();
        System.Drawing.Color c = System.Drawing.Color.Blue;

        internal statType() { }
        public statType(string name, string moduleType, System.Drawing.Color c, stats.displaymode displayMode = stats.displaymode.line, stats.statmode statMode = stats.statmode.increment)
        {
            this.name = name;
            this.moduleType = moduleType;
            this.displayMode = displayMode;
            this.statMode = statMode;
        }

        public void updateDisplaySettings(System.Drawing.Color c, stats.displaymode displayMode = stats.displaymode.line, stats.statmode statMode = stats.statmode.increment)
        {
            this.c = c;
            this.displayMode = displayMode;
            this.statMode = statMode;
        }

        public statSlice getSlice()
        {
            return getSlice(DateTime.Now);
        }

        public statSlice getSlice(DateTime time)
        {
            List<statSlice> matches = statSlices.Where(x => time > x.timeSlice && time < x.timeSlice.Add (stats.granularity)).ToList();
            if (matches.Count == 0)
            {
                statSlice s = new statSlice(time);
                statSlices.Add(s);
                return s;
            }
            else if (matches.Count == 1)
            {
                return matches[0];
            }
            else
            {
                Roboto.log.log("More than one match for timeslice!", logging.loglevel.warn);
                return matches[0];
            }
        }

        public void logStat(statItem item)
        {
            statSlice slice = getSlice();
            if (statMode == stats.statmode.increment)
            {
                slice.addCount(item.items);
            }
            else if (statMode == stats.statmode.absolute)
            {
                slice.setCount(item.items);
            }
        }

        /// <summary>
        /// Gather this stat's datapoints for use on a graph - used to build a System.Windows.Forms.
        /// DataVisualization.Charting.Series directly; that type doesn't exist outside WinForms, so
        /// this now returns the same underlying (title, points, color, line-vs-bar) data as a plain
        /// DTO instead. Chart rendering itself is a stub pending the ScottPlot port (see
        /// stats.generateImage) - kept the data-gathering loop as-is since it's the reusable part.
        /// </summary>
        /// <param name="startTime"></param>
        /// <returns></returns>
        public statSeriesData getSeries(DateTime startTime)
        {
            string title = this.moduleType.StartsWith("Roboto.") ? this.moduleType.Substring(7) : this.moduleType;
            statSeriesData s = new statSeriesData(title + ">" + this.name, c, displayMode);

            for (int i = 0; i < stats.graphYAxisCount; i++)
            {
                DateTime point = startTime.Subtract(TimeSpan.FromTicks(stats.granularity.Ticks * i));
                statSlice slice = getSlice(point);
                if (slice != null)
                {
                    s.points.Add((point.Subtract(startTime).TotalHours, slice.count));
                }

            }

            return s;
        }

        public void removeOldData()
        {
            DateTime cutoff = DateTime.Now.Subtract(new TimeSpan(stats.granularity.Ticks * stats.graphYAxisCount));
            statSlices.RemoveAll(x => x.timeSlice < cutoff);
        }


    }

    /// <summary>Plain data carried out of statType.getSeries - see that method's comment. Chart-
    /// library-agnostic on purpose, pending the ScottPlot port.</summary>
    public class statSeriesData
    {
        public string title;
        public System.Drawing.Color color;
        public stats.displaymode displayMode;
        public List<(double hoursAgo, double value)> points = new List<(double, double)>();

        public statSeriesData(string title, System.Drawing.Color color, stats.displaymode displayMode)
        {
            this.title = title;
            this.color = color;
            this.displayMode = displayMode;
        }
    }

    public class statSlice
    {
        public DateTime timeSlice = DateTime.MinValue;
        public int count = 0;
        internal statSlice() { }
        public statSlice(DateTime timeSlice)
        {
            //round the time down to the nearest x mins. 
            var delta = timeSlice.Ticks % stats.granularity.Ticks;
            this.timeSlice = new DateTime(timeSlice.Ticks - delta, timeSlice.Kind);
        }
        public void addCount(int items)
        {
            count += items;
        }

        public void setCount(int items)
        {
            count = items;
        }
    }

    //an incoming item
    public class statItem
    {
        public string statTypeName;
        public string moduleType;
        public int items;
        public statItem (string statTypeName, Type moduleType, int items = 1)
        {
            this.statTypeName = statTypeName;
            this.moduleType = moduleType.ToString();
            this.items = items;
        }
    }

    /// <summary>
    /// Stats DB that is attached to settings. Used to store all incoming stats, and generate images. 
    /// </summary>
    public class stats
    {
        //constants
        public static TimeSpan granularity = new TimeSpan(0, 15, 0); //15 mins
        public static int graphYAxisCount = 192;
        public enum displaymode { line, bar };
        public enum statmode { increment, absolute };

        //data
        public List<statType> statsList = new List<statType>();

        /// <summary>
        /// Called during system startup. Adds some default types, and registers a "startup" event
        /// </summary>
        public void startup()
        {
            //TODO - temporary code - replace any incorrect namespaces following renamespacing and remove duplicates
            List<statType> newTypes = new List<statType>();
            foreach (statType t in statsList)
            {
                /*if (t.moduleType.StartsWith("Roboto."))
                {
                    t.moduleType = "RobotoChatBot." + t.moduleType.Remove(0, 7);
                }*/

                if (newTypes.Where(x => x.moduleType == t.moduleType && x.name == t.name).ToList().Count() > 0)
                {
                    Roboto.log.log("Stat type " + t.moduleType + " already exists! Dropping", logging.loglevel.critical);
                }
                else
                {
                    newTypes.Add(t);
                }
                
            }
            //swap in the rebuild list
            statsList = newTypes;



            registerStatType("Startup", typeof(Roboto), Color.LawnGreen, displaymode.bar);
            registerStatType("Incoming Msgs", typeof(TelegramAPI), Color.Blue );
            registerStatType("Outgoing Msgs", typeof(TelegramAPI), Color.Purple);
            registerStatType("BotAPI Timeouts", typeof(Roboto), Color.Azure, stats.displaymode.bar);
            registerStatType("Hammering Prevention", typeof(Roboto), Color.Turquoise, stats.displaymode.bar);
            registerStatType("Chats Purged", typeof(Roboto), Color.DarkRed, displaymode.bar);

            logStat(new statItem("Startup", typeof(Roboto)));
        }


        // statsList (and each statType's own statSlices) is another shared structure touched by
        // both the message thread (every incoming/outgoing message bumps a counter) and the phase-4
        // background scheduler thread (every module's own backgroundProcessing calls logStat too) -
        // same GlobalListsKey convention as everywhere else this pass touches shared state. All
        // in-memory, no network I/O, so holding the lock across a whole method body here is fine.
        public void registerStatType(string name, Type moduleType, System.Drawing.Color c, stats.displaymode displayMode = stats.displaymode.line, stats.statmode statMode = statmode.increment )
        {
            using (ChatKeyedLock.Acquire(ChatKeyedLock.GlobalListsKey))
            {
                statType existing = getStatTypeUnlocked(name, moduleType.ToString());
                if (existing != null)
                {
                    Roboto.log.log("Registering StatType " + name + " from " + moduleType.ToString() + ":  already exists.", logging.loglevel.normal);
                    existing.updateDisplaySettings(c, displayMode, statMode);
                }
                else
                {
                    statType newST = new statType(name, moduleType.ToString(), c, displayMode, statMode);
                    statsList.Add(newST);
                    Roboto.log.log("Registering StatType " + name + " from " + moduleType.ToString() + " added.", logging.loglevel.warn);
                }
            }

        }

        public void logStat(statItem item)
        {
            using (ChatKeyedLock.Acquire(ChatKeyedLock.GlobalListsKey))
            {
                statType type = getStatTypeUnlocked(item.statTypeName, item.moduleType.ToString());
                if (type != null)
                {
                    type.logStat(item);
                }
                else
                {
                    Roboto.log.log("Tried to log stat " + item.statTypeName + " for " + item.moduleType + " but doesnt exist!", logging.loglevel.high);
                }
            }
        }

        private statType getStatType(string name, string moduleType)
        {
            using (ChatKeyedLock.Acquire(ChatKeyedLock.GlobalListsKey))
            {
                return getStatTypeUnlocked(name, moduleType);
            }
        }

        /// <summary>Lock-free inner version, for callers (registerStatType/logStat above) that
        /// already hold GlobalListsKey on the same thread - Monitor's reentrancy would make calling
        /// the public getStatType() safe too, but this avoids a pointless double-acquire on a hot
        /// path (every single incoming/outgoing message goes through logStat).</summary>
        private statType getStatTypeUnlocked(string name, string moduleType)
        {
            List<statType> matches = statsList.Where(x => x.name == name && x.moduleType == moduleType).ToList();
            if (matches.Count == 1 ) { return matches[0]; }
            else if (matches.Count > 1 )
            {
                Roboto.log.log("More than one match for stat " + name + " in " + moduleType, logging.loglevel.high);
                return matches[0];
            }
            else
            {
                return null;
            }

        }

        /// <summary>
        /// get all stats matching a pattern
        /// </summary>
        /// <param name="regex"></param>
        /// <returns></returns>
        private List<statType> getStatTypes (string regex)
        {
            using (ChatKeyedLock.Acquire(ChatKeyedLock.GlobalListsKey))
            {
                List<statType> matches = new List<statType>();
                try
                {
                    Regex r = new Regex(regex, RegexOptions.IgnoreCase);
                    foreach (statType t in statsList)
                    {

                        Match m = r.Match(t.moduleType + ">" + t.name);
                        if (m.Success)
                        {
                            matches.Add(t);
                        }

                    }
                }
                catch
                {
                    //will probably get some regex errors here - ignore them.
                    Roboto.log.log("Error parsing statType. Probably a regex issue", logging.loglevel.warn);
                }
                return matches;
            }
        }


        /// <summary>
        /// Expecting a list of series names, which are the type and name, split with an ">", or can be a list of regex's
        /// </summary>
        /// <param name="series"></param>
        /// <remarks>
        /// STUB pending the ScottPlot port (this codebase's rewrite branch already has a working
        /// ScottPlot renderer to model this on - see src/Roboto.Bot/Commands/StatGraphCommand.cs on
        /// rewrite/dotnet-docker-port). The old System.Windows.Forms.DataVisualization.Charting
        /// implementation (Chart/ChartArea/Series/Legend, SaveImage to a Stream) can't run outside
        /// WinForms at all. getMatchingSeries below still does the real series-selection logic
        /// (exact + regex match against "moduleType>name") and each match's statType.getSeries(...)
        /// still gathers real datapoints (as statSeriesData, chart-library-agnostic) - only the
        /// actual image rendering is missing. /statgraph (mod_standard.cs) will report "no image"
        /// until this is filled in.
        /// </remarks>
        public Stream generateImage(List<string> series)
        {
            List<statType> matches = getMatchingSeries(series);
            if (matches.Count == 0)
            {
                Roboto.log.log("No chart type matches", logging.loglevel.warn);
                return null;
            }

            DateTime graphStartTime = DateTime.Now;
            List<statSeriesData> seriesData = matches.Select(m => m.getSeries(graphStartTime)).ToList();

            Roboto.log.log($"generateImage: chart rendering not yet ported off WinForms - {seriesData.Count} series ready, no image produced. See this method's doc comment.", logging.loglevel.warn);
            return null;
        }

        /// <summary>Resolves a list of "moduleType>name" exact matches / regexes (or none, meaning
        /// "everything") against the registered statTypes. Split out of generateImage so the
        /// series-selection logic - the part with no charting-library dependency at all - survives
        /// untouched once rendering itself is ported to ScottPlot.</summary>
        private List<statType> getMatchingSeries(List<string> series)
        {
            //if nothing passed in, assume all stats
            if (series.Count == 0) { series = new List<string> { ".*" }; }

            List<statType> matches = new List<statType>();
            foreach (string s in series)
            {
                //populate list of statTypes that match our query. Dont worry about order / dupes - will be ordered later
                //try exact matches
                string[] titles = s.Trim().Split(">"[0]);
                if (titles.Length == 2)
                {
                    //get the series info
                    statType seriesStats = getStatType(titles[1], titles[0]);
                    if (seriesStats != null) { matches.Add(seriesStats); }
                }

                //try regex matches
                List<statType> matchingTypes = getStatTypes(s);
                foreach (statType mt in matchingTypes)
                {
                    matches.Add(mt);
                }

            }

            return matches.Distinct().OrderBy(x => x.moduleType + ">" + x.name).ToList();
        }

        public void houseKeeping()
        {
            using (ChatKeyedLock.Acquire(ChatKeyedLock.GlobalListsKey))
            {
                //Ditch any stats
                foreach (statType s in statsList)
                {
                    s.removeOldData();
                }
            }


        }


        

    }
}
