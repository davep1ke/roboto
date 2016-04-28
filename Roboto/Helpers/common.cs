using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Roboto.Helpers
{
    static class common
    {
        private static TimeSpan oneDay = new TimeSpan(1, 0, 0, 0);
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
                Roboto.log.log("No quiet hours, defaulting", logging.loglevel.verbose);
                return startTime.Add(timeToAdd);
            }


            Roboto.log.log("Adding " + timeToAdd.ToString("c") + " to " + startTime.ToString("f") + " ignoring " + startQuietHours.ToString("c") + " to " + endQuietHours.ToString("c"), logging.loglevel.verbose);
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

                Roboto.log.log("Currently " + endTime.ToString("f") + (timeToStart < timeToEnd? "(L)" : "(Q)")  + ". Start is in " + timeToStart.ToString("c") + ", end in " + timeToEnd.ToString("c") + ". " + timeToAdd.ToString("c") + " remaining" , logging.loglevel.verbose);
                
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


            Roboto.log.log("Result was " + endTime.ToString("f"), logging.loglevel.verbose);
            return endTime;

         }
    }
}
