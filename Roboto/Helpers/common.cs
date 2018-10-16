using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RobotoChatBot.Helpers
{
    static class common
    {
        private static TimeSpan oneDay = new TimeSpan(1, 0, 0, 0);

        /// <summary>
        /// Cleanse some text so that it is matchable. Limit to first n characters. 
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        public static string cleanseText(string s, int maxLength = -1)
        {
            try
            {

                s = new string(s.Where(c => !char.IsPunctuation(c)).ToArray());
                s = s.ToUpper().Trim();
                s = s.Replace(" ", string.Empty);
                s = s.Replace("\t", string.Empty);

                if (maxLength != -1 && s.Length > maxLength) { s = s.Substring(0, maxLength); }
                return s;
            }

            catch (Exception e)
            {
                Roboto.log.log("Error cleansing string '" + s + "' - " + e.ToString(), logging.loglevel.critical);
                //if we fail for wahtever reason, fall back to the original string.
                return s;
            }
        }



        /// <summary>
        /// Gets card positions that havent already been picked. 
        /// </summary>
        /// <param name="p"></param>
        /// <param name="questions"></param>
        /// <returns></returns>
        public static List<int> getUniquePositions(int arraySize, int questions)
        {
            if (questions > arraySize) { questions = arraySize; }
            if (questions == -1) { questions = arraySize; }
            if (questions > 500) { questions = 500; }

            //TODO - generic
            List<int> results = new List<int>();
            //create a dummy array

            List<int> dummy = new List<int>();
            for (int i = 0; i < arraySize; i++) { dummy.Add(i); }

            //pick from the array, removing the picked number
            for (int i = 0; i < questions; i++)
            {
                int newCardPos = settings.getRandom(dummy.Count);
                results.Add(dummy[newCardPos]);
                dummy.Remove(newCardPos);
            }

            return results;
        }



        /// <summary>
        /// Add a time to a datetime, ignoring any "quiet"  periods
        /// </summary>
        /// <param name="startTime"></param>
        /// <param name="startQuietHours"></param>
        /// <param name="endQuietHours"></param>
        /// <param name="timeToAdd"></param>
        /// <returns></returns>
        public static DateTime addTimeIgnoreQuietHours(DateTime startTime, TimeSpan startQuietHours, TimeSpan endQuietHours, TimeSpan timeToAdd)
        {
            
            if (startQuietHours == TimeSpan.MinValue || endQuietHours == TimeSpan.MinValue)
            {
                //Roboto.log.log("Adding " + timeToAdd.ToString("c") + " to " + startTime.ToString("f") + " with no quiet hours set", logging.loglevel.verbose);
                return startTime.Add(timeToAdd);
            }

            String logtext = "Adding " + timeToAdd.ToString("c") + " to " + startTime.ToString("f") + " ignoring " + startQuietHours.ToString("c") + " to " + endQuietHours.ToString("c") + ". ";
            DateTime endTime = startTime;
            
            //loop through and subtract time from the timeToAdd, and move the endTime forwards;
            while(timeToAdd > TimeSpan.Zero )
            {
                //ignore the date for now - go off times. 
                TimeSpan currentTimePart = new TimeSpan(endTime.Hour, endTime.Minute, endTime.Second);

                //is the start or the end next? 
                TimeSpan timeToStart = startQuietHours.Subtract(currentTimePart);
                if (timeToStart == TimeSpan.Zero) { timeToStart = oneDay; }
                if (timeToStart < TimeSpan.Zero) { timeToStart = timeToStart + oneDay; }
                TimeSpan timeToEnd = endQuietHours.Subtract(currentTimePart);
                if (timeToEnd == TimeSpan.Zero) { timeToEnd = oneDay; }
                if (timeToEnd < TimeSpan.Zero) { timeToEnd = timeToEnd + oneDay; }

                //Roboto.log.log("Currently " + endTime.ToString("f") + (timeToStart < timeToEnd? "(L)" : "(Q)")  + ". Start is in " + timeToStart.ToString("c") + ", end in " + timeToEnd.ToString("c") + ". " + timeToAdd.ToString("c") + " remaining" , logging.loglevel.verbose);
                
                if (timeToStart < timeToEnd || timeToStart == timeToEnd)
                {
                    //start of quiet period happens next - we are in a "live" period
                    //add time to our start, and remove from our timeToAdd

                    //are we about to run out of time? Only add the remaining time. 
                    if (timeToStart > timeToAdd) { timeToStart = timeToAdd; }

                    timeToAdd = timeToAdd.Subtract(timeToStart);
                    endTime = endTime.Add(timeToStart);
                }
                else
                {
                    //end of quiet period happens next, we are in a "quiet" period
                    //add time to our start, ignore end.
                    endTime = endTime.Add(timeToEnd);
                }
                
            }

            Roboto.log.log(logtext + "Result was " + endTime.ToString("f"), logging.loglevel.verbose);
            return endTime;

         }

        public static string removeMarkDownChars(string text)
        {
            text = text.Replace("_", "-");
            text = text.Replace("*", "x");
            text = text.Replace("'", "\"");
            text = text.Replace("`", "\"");

            return text;
        }

        public static string escapeMarkDownChars(string text)
        {
            text = text.Replace("_", "\\_");
            text = text.Replace("*", "\\*");
            text = text.Replace("'", "\\'");
            text = text.Replace("`", "\\`");

            return text;
        }
    }
}
