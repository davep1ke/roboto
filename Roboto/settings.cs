using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Media;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using RobotoChatBot.Modules;

namespace RobotoChatBot
{


    public class settings
    {
        public static string foldername = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\Roboto\";
        private static string filename = foldername;

        //logging
        public bool enableFileLogging = true;
        public int rotateLogsEveryXHours = 12;
        public int saveXMLeveryXMins = 30;
        public int killInactiveChatsAfterXDays = 30;
        public int purgeInactiveChatsAfterXDays = 100;
        public int chatPresenceExpiresAfterHours = 96;

        
        //stats database
        public stats stats = new stats();

        //public List<replacement> replacements = new List<replacement>();

        public string telegramAPIURL = "https://api.telegram.org/bot";
        public string telegramAPIKey = "ENTERYOURAPIKEYHERE";
        public string botUserName = "Roboto_bot_name";
        public int waitDuration = 60; //wait duration for long polling. 
        public int lastUpdate = 0; //last update index, needs to be passed back with each call. 
        public int maxLogItems = 50;

        //generic plugin storage. NB: Chats DO want to be serialised. 
        public List<Modules.RobotoModuleDataTemplate> pluginData = new List<Modules.RobotoModuleDataTemplate>();
        public List<chat> chatData = new List<chat>();

        //Random generator
        static Random randGen = new Random();

        //list of expected replies
        public List<ExpectedReply> expectedReplies = new List<ExpectedReply>();
        public List<chatPresence> RecentChatMembers = new List<chatPresence>();

        //is this the first time the settings file has been initialised?
        public bool isFirstTimeInitialised = false;




        /// <summary>
        /// Load all our data from XML
        /// </summary>
        /// <returns></returns>
        public static settings load()
        {
            //set the filename based on the current context (instance)
            if (Roboto.context == null)
            {
                filename += "settings.xml";


            }
            else { filename += Roboto.context + ".xml"; }

            Roboto.log.log("Loading from " + filename, logging.loglevel.high);

            //load the file
            try
            {

                XmlSerializer deserializer = new XmlSerializer(typeof(settings), Plugins.getPluginDataTypes());
                TextReader textReader = new StreamReader(filename);
                settings setts = (settings)deserializer.Deserialize(textReader);
                textReader.Close();
                return setts;
            }


            catch (Exception e)
            {
                if (e is System.IO.FileNotFoundException || e is System.IO.DirectoryNotFoundException)
                {
                    //create a new one
                    settings sets = new settings();
                    sets.isFirstTimeInitialised = true;
                    return sets;
                }
                else
                {
                    Roboto.log.log("Bad XML File - please fix and restart. " + e.ToString(), logging.loglevel.critical);
                }
            }
            return null;

        }




        


        /*// <summary>
        /// Make sure any reply processing is being done
        /// </summary>
        public void expectedReplyHousekeeping()
        {

            
        }*/



        /// <summary>
        /// Save all data to XML
        /// </summary>
        public void save()
        {
            //as we are saving (and presumably exiting) we dont need to worry that this is a first time file anymore
            isFirstTimeInitialised = false;

            //create folder if doesnt exist:
            DirectoryInfo di = new DirectoryInfo(foldername);
            if (!di.Exists)
            {
                di.Create();
            }

            //use datepart to keep a file for each day. 
            string datePart = DateTime.Now.ToString("yyyy-MM-dd") + ".xml";

            //delete our old backup
            FileInfo fi = new FileInfo(filename + "." + datePart);
            if (fi.Exists) { fi.Delete(); }

            //replace our current backup
            FileInfo fi_backup = new FileInfo(filename);
            if (fi_backup.Exists) { fi_backup.MoveTo(filename + "." + datePart); }


            //write out XML
            XmlSerializer serializer = new XmlSerializer(typeof(settings), Plugins.getPluginDataTypes());
            TextWriter textWriter = new StreamWriter(filename);
            serializer.Serialize(textWriter, this);
            textWriter.Close();
        }



        public static int getRandom(int maxInt)
        {
            return randGen.Next(maxInt);
        }


    }

}
