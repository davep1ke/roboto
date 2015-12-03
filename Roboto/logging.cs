using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Roboto
{
    /// <summary>
    /// Class for logging data to the console window in a standard format. 
    /// </summary>
    public class logging
    {
        public enum loglevel { verbose, low, normal, warn, high, critical }
        private bool followOnLine = false;
        private Char bannerChar = "*".ToCharArray()[0];

        public void log (string text, loglevel level = loglevel.normal, ConsoleColor colour = ConsoleColor.White, bool noLineBreak = false, bool banner = false, bool pause = false, bool skipheader = false)
        {

            StackFrame frame = new StackFrame(1);
            var method = frame.GetMethod();
            string classtype = method.DeclaringType.ToString();
            string methodName = method.Name;

            
            //guess colour
            if (colour == ConsoleColor.White)
            {
                switch(level)
                {
                    case loglevel.verbose:
                        break;
                    case loglevel.low:
                        colour = ConsoleColor.DarkGreen;
                        break;
                    case loglevel.normal:
                        colour = ConsoleColor.Blue;
                        break;
                    case loglevel.warn:
                        colour = ConsoleColor.Magenta;
                        break;
                    case loglevel.high:
                        colour = ConsoleColor.Yellow;
                        break;
                    case loglevel.critical:
                        colour = ConsoleColor.Red;
                        break;
                }
            }

            Console.ForegroundColor = colour;
            
            if (noLineBreak)
            {
                Console.Write(text);
                followOnLine = true;
            }
            else
            {
                //clear any trailing lines from write's instead of writelines
                if (followOnLine)
                {
                    Console.WriteLine();
                    followOnLine = false;
                }
                //add our time and module stamps
                string outputString = DateTime.Now.ToString("dd-MM-yyyy  h:mm:ss") ;
                if (banner == false || skipheader == true)
                {
                    outputString += " - " 
                        + classtype.ToString().PadRight(25)
                        + methodName.PadRight(20)
                        +  " - ";
                    
                }
                else
                {
                    outputString += "".PadRight(40);
                }

                //add the main text
                outputString +=  text;

                //add a row of *** for a banner
                if (banner) { Console.WriteLine("".PadLeft(Console.WindowWidth-1, bannerChar)); }
                //write the main line
                Console.WriteLine(outputString);
                //another row of ***
                if (banner) { Console.WriteLine("".PadLeft(Console.WindowWidth-1, bannerChar)); }
                //pause if needed
                if (pause)
                {
                    Console.WriteLine("Press any key to continue");
                    Console.ReadKey();
                }

            }
            Console.ResetColor();
        }

        public void setTitle(string title)
        {
            Console.Title = "Roboto - " + title;
        }
    }
}
