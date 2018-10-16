using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RobotoChatBot
{
    /*public class replacement 
    {
        public string source;
        public string replace;

        public replacement(string source, string replace)
        {
            this.source = source;
            this.replace = replace;
        }

        //null constructor for serialization
        protected replacement() { }

    }*/


    public class grabbedFile
    {
        public string filename;
        public DateTime date;

        public grabbedFile(string filename)
        {
            this.filename = filename;
            this.date = DateTime.Now;
        }

        //null constructor for serialization
        protected grabbedFile() { }

    }
}
