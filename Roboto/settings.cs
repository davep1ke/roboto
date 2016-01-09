using System;
using System.Collections.Generic;
using System.Reflection;

using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace Roboto
{
    

    public class settings
    {
        public static string foldername = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\Roboto\";
        private static string filename = foldername;
        public bool enableFileLogging = true;

        //module list. Static, as dont want to serialise the plugins, just the data.
        public static List<Modules.RobotoModuleTemplate> plugins = new List<Modules.RobotoModuleTemplate>();
        //stats database
        public stats stats = new stats();

        //public List<replacement> replacements = new List<replacement>();

        public string telegramAPIURL;
        public string telegramAPIKey;
        public string botUserName = "";
        public int waitDuration = 60; //wait duration for long polling. 
        public int lastUpdate = 0; //last update index, needs to be passed back with each call. 
        
        //generic plugin storage. NB: Chats DO want to be serialised. 
        public List<Modules.RobotoModuleDataTemplate> pluginData = new List<Modules.RobotoModuleDataTemplate>();
        public List<chat> chatData = new List<chat>();

        //Random generator
        static Random randGen = new Random();

        //list of expected replies
        public List<ExpectedReply> expectedReplies = new List<ExpectedReply>();

        //is this the first time the settings file has been initialised?
        public bool isFirstTimeInitialised = false;

        /// <summary>
        /// Load all the plugins BEFORE loading the settings file. We need to be able to enumerate the extra types when loading the XML. 
        /// </summary>
        public static void loadPlugins()
        {
            //load all plugins by looking for all objects derived from the abstract class. 
            Assembly currAssembly = Assembly.GetExecutingAssembly();

            foreach (Type type in currAssembly.GetTypes())
            {
                
                if (type.IsClass && !type.IsAbstract
                    //is this a subclass of the module template
                    && type.IsSubclassOf(typeof(Modules.RobotoModuleTemplate))
                    //is our plugin filter disabled?
                    && (Roboto.pluginFilter.Count == 0
                    //or is this plugin listed?
                    || Roboto.pluginFilter.Contains(type.Name))
                    )
                {
                   
                    Roboto.log.log( "Registering plugin " + type.Name, logging.loglevel.low);

                    if (pluginExists(type))
                    {
                        //TODO - this is going to be looking for the template, not the datatemplate!
                        //Console.WriteLine("Registering plugin " + type.Name);
                    }
                    else
                    {
                        Modules.RobotoModuleTemplate plugin = (Modules.RobotoModuleTemplate)Activator.CreateInstance(type);
                        Roboto.log.log( "Added " + plugin.GetType().ToString(), logging.loglevel.low);
                        plugins.Add(plugin);
                        plugin.init();
                    }
                    
                }
            }
        }

        /// <summary>
        /// Perform any housekeeping / startup checks
        /// </summary>
        public void startupChecks()
        {
            foreach(Modules.RobotoModuleTemplate plugin in plugins )
            {
                plugin.startupChecks();
            }
            stats.startup();
        }


        /// <summary>
        /// Basic checks on the data. 
        /// </summary>
        public void validate()
        {
            foreach (Modules.RobotoModuleTemplate plugin in plugins)
            {
                plugin.initData(); //this data probably already exists if loaded by XML, but if not, allow the plugin to create it. 
                if (plugin.pluginDataType != null)
                {
                    //TODO - check if this datatype is a subclass of RobotoModuleDataTemplate
                }
                //TODO - do same for chat data types. 
            }


            if (telegramAPIURL == null) {telegramAPIURL = "https://api.telegram.org/bot";};
            if (telegramAPIKey == null) { telegramAPIKey = "ENTERYOURAPIKEYHERE"; };
            if (botUserName == "") { botUserName = "Roboto_bot_name"; }

            Console.WriteLine("=========");
            Console.WriteLine("All Plugins initialised");
            Console.WriteLine(Modules.mod_standard.getAllMethodDescriptions());
            Console.WriteLine("=========");


            foreach (chat c in chatData)
            {
                c.initPlugins();
            }

        }

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

            Roboto.log.log( "Loading from " + filename, logging.loglevel.high);

            //load the file
            try
            {

                XmlSerializer deserializer = new XmlSerializer(typeof(settings), getPluginDataTypes());
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
                    Console.WriteLine(e.ToString());
                }
            }
            return null;

        }

        /// <summary>
        /// Does the user have any outstanding expected Replies?
        /// </summary>
        /// <param name="playerID"></param>
        /// <returns></returns>
        public bool userHasOutstandingMessages(long playerID)
        {
            foreach (ExpectedReply e in expectedReplies)
            {
                if (e.userID == playerID) { return true; }
            }
            return false;
        }

        /// <summary>
        /// Add a new expected reply to the stack. Should be called internally only - New messages should be sent via TelegramAPI.GetExpectedReply
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        public long newExpectedReply(ExpectedReply e)
        {
            //check if we can send it? Get the messageID back
            long messageID = -1;
            if (!userHasOutstandingMessages(e.userID))
            {
                //send the message, grab the ID. 
                messageID = e.sendMessage();

            }

            //either way, chuck it on the stack
            expectedReplies.Add(e);
            
            return messageID;

        }

        /// <summary>
        /// Clear the expected Replies for a given plugin
        /// </summary>
        /// <param name="chat_id"></param>
        /// <param name="pluginType"></param>
        public void clearExpectedReplies(long chat_id, Type pluginType)
        {
            //find replies for this chat, and add them to a temp list
            List<ExpectedReply> repliesToRemove = new List<ExpectedReply>();
            foreach (ExpectedReply reply in expectedReplies)
            {
                if (reply.chatID == chat_id && reply.isOfType(pluginType)) { repliesToRemove.Add(reply); }
            }
            //now remove them
            foreach (ExpectedReply reply in repliesToRemove)
            {
                expectedReplies.Remove(reply);
            }
        }


        /// <summary>
        /// Get all the custom types used, for serialising / deserialising data to XML.
        /// </summary>
        /// <returns></returns>
        public static Type[] getPluginDataTypes()
        {
            //put into a list first
            List<Type> customTypes = new List<Type>();
            foreach (Modules.RobotoModuleTemplate plugin in plugins)
            {
                if (plugin.pluginDataType != null) { customTypes.Add(plugin.pluginDataType);}
                if (plugin.pluginChatDataType != null) { customTypes.Add(plugin.pluginChatDataType); }
            }
            
            return customTypes.ToArray();
        }

        /// <summary>
        /// Make sure any reply processing is being done
        /// </summary>
        public void expectedReplyHousekeeping()
        {
            //Build up a list of user IDs
            //List<int> userIDs = new List<int>();
            //foreach (ExpectedReply e in expectedReplies) { userIDs.Add(e.userID); }
            //userIDs = (List<int>)userIDs.Distinct<int>();
            List<long> userIDs = expectedReplies.Select(e => e.userID).Distinct().ToList<long>();
            
            foreach (long userID in userIDs)
            {
                List<ExpectedReply> userReplies = expectedReplies.Where(e => e.userID == userID).ToList();
                
                //for each user, check if a message has been sent, and track the oldest message
                ExpectedReply oldest = null;
                bool sent = false;
                foreach (ExpectedReply e in userReplies)
                {
                    if (e.isSent()) { sent = true; }
                    else
                    {
                        if (oldest == null || e.timeLogged < oldest.timeLogged )
                        {
                            oldest = e;
                        }
                    }
                }

                //send the message if neccessary
                if (!sent && oldest != null)
                {
                    oldest.sendMessage();
                }

            }
            
        }

        /// <summary>
        /// Get an array of expected replies for a given plugin
        /// </summary>
        /// <param name="chatID"></param>
        /// <param name="userID"></param>
        /// <param name="filter"></param>
        /// <returns></returns>
        public List<ExpectedReply> getExpectedReplies(Type pluginType, long chatID, long userID = -1, string filter = "")
        {
            List<ExpectedReply> responses = new List<ExpectedReply>();
            foreach (ExpectedReply e in expectedReplies)
            {
                if (e.isOfType(pluginType)
                    && e.chatID == chatID
                    && (userID == -1 || e.userID == userID)
                    && (filter == "" || filter.Contains(e.messageData))
                    )
                {
                    responses.Add(e);


                }

            }
            return responses;
        }

        public bool parseExpectedReplies(message m)
        {
           
            //are we expecteing this? 
            bool processed = false;
            Modules.RobotoModuleTemplate pluginToCall = null;
            ExpectedReply er = null;
            foreach (ExpectedReply e in expectedReplies)
            {
                //we are looking for direct messages from the user where c_id = m_id, OR reply messages where m_id = reply_id
                //could trigger twice if we fucked something up - dont think this is an issue but checking processed flag for safety
                if (!processed && e.isSent() && m.userID == e.userID)
                {
                    if (m.chatID == e.userID || m.replyMessageID == e.outboundMessageID)
                    {
                        processed = true;
                        //find the plugin, send the expectedreply to it
                        foreach (Modules.RobotoModuleTemplate plugin in settings.plugins)
                        {
                            if (e.isOfType(plugin.GetType()))
                            {
                                //stash these for calling outside of the "foreach" loop. This is so we can be sure it is called ONCE only, and so that we can remove
                                //the expected reply before calling the method, so any post-processing works smoother.
                                pluginToCall = plugin;
                                er = e;
                            }
                        }
                    }
                }
            }

            

            if (processed)
            {
                expectedReplies.Remove(er);
                //now send it to the plugin (remove first, so any checks can be done)
                bool pluginProcessed = pluginToCall.replyReceived(er, m);

                if (!pluginProcessed)
                {
                    throw new InvalidProgramException("Plugin didnt process the message it expected a reply to!");
                        
                }


                //are there any more messages for the user? If so, find & send
                ExpectedReply messageToSend = null;
                foreach (ExpectedReply e in expectedReplies)
                {
                    if (e.userID == m.userID)
                    {
                        if (messageToSend == null || e.timeLogged < messageToSend.timeLogged)
                        {
                            messageToSend = e;
                        }

                    }
                }

                //send it
                if ( !userHasOutstandingMessages(m.userID) && messageToSend != null)
                {
                    messageToSend.sendMessage();
                }
                
            }
            return processed;   
            
        }


        /// <summary>
        /// Handle a failed outbound message that a plugin expects a reply for. 
        /// </summary>
        /// <param name="er"></param>
        public void parseFailedReply(ExpectedReply er)
        {

            expectedReplies.Remove(er);
            Modules.RobotoModuleTemplate pluginToCall = null;

            foreach (Modules.RobotoModuleTemplate plugin in settings.plugins)
            {
                if (er.pluginType == plugin.GetType().ToString())
                {
                    //stash these for calling outside of the "foreach" loop. This is so we can be sure it is called ONCE only, and so that we can remove
                    //the expected reply before calling the method, so any post-processing works smoother.
                    pluginToCall = plugin;
                }
            }
            //now send it to the plugin (remove first, so any checks can be done)
            bool pluginProcessed = pluginToCall.replyReceived(er, null, true);

            if (!pluginProcessed)
            {
                Roboto.log.log("Plugin " + pluginToCall.GetType().ToString() + " didnt process the message it expected a reply to!", logging.loglevel.high);
                throw new InvalidProgramException("Plugin didnt process the message it expected a reply to!");

            }
            
        }




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

            //write out XML
            XmlSerializer serializer = new XmlSerializer(typeof(settings), getPluginDataTypes());
            TextWriter textWriter = new StreamWriter(filename);
            serializer.Serialize(textWriter, this);
            textWriter.Close();
        }



        public static int getRandom(int maxInt)
        {
            return randGen.Next(maxInt);
        }



        

        public int getUpdateID()
        {
            return lastUpdate + 1;
        }

        public void registerData(Modules.RobotoModuleDataTemplate data)
        {

            if (typeDataExists(data.GetType()) == false)
            {
                pluginData.Add(data);
                Console.WriteLine("Added data of type " + data.GetType().ToString());
            }
            else
            {
                Console.WriteLine("Plugin data of type " + data.GetType().ToString() + " already exists!");
            }

        }

        /// <summary>
        /// Check if a plugins datastore exists
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public bool typeDataExists(Type t)
        {
            bool found = false;
            foreach (Modules.RobotoModuleDataTemplate existing in pluginData)
            {
                if (t.GetType() == existing.GetType())
                {
                    
                    found = true;
                }
            }
            return found;
        }

        /// <summary>
        /// check if a plugin Type exists
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public static bool pluginExists(Type t)
        {
            bool found = false;
            foreach (Modules.RobotoModuleTemplate existing in plugins)
            {
                if (t.GetType() == existing.GetType())
                {

                    found = true;
                }
            }
            return found;
        }

        public T getPluginData<T>()
        {
            foreach (Modules.RobotoModuleDataTemplate existing in pluginData)
            {
                if (existing.GetType() == typeof(T))
                {
                    //Console.WriteLine("Plugin data of type " + data.GetType().ToString() + " already exists!");
                    T retVal = (T) Convert.ChangeType(existing, typeof(T));
                    return retVal;
                }
            }

            Console.WriteLine("Couldnt find plugin data of type " + typeof(T).ToString());
            throw new InvalidDataException("Couldnt find plugin data of type " + typeof(T).ToString());
            
        }

        public void removeReply(ExpectedReply r)
        {
            expectedReplies.Remove(r);
        }

        public Modules.RobotoModuleDataTemplate getPluginData(Type pluginDataType)
        {
            foreach (Modules.RobotoModuleDataTemplate existing in pluginData)
            {
                if (existing.GetType() == pluginDataType)
                {
                    return existing;
                }
            }

            Console.WriteLine("Couldnt find plugin data of type " + pluginDataType.ToString());
            throw new InvalidDataException("Couldnt find plugin data of type " + pluginDataType.ToString());
        }

        /// <summary>
        /// find a chat by its chat ID
        /// </summary>
        /// <param name="chat_id"></param>
        /// <returns></returns>
        public chat getChat(long chat_id)
        {
            foreach (chat c in chatData)
            {
                if (c.chatID == chat_id)
                {
                    return c;
                }
            }
            return null;
        }

        /// <summary>
        /// Add data about a chat to the store. 
        /// </summary>
        /// <param name="chat_id"></param>
        public chat addChat(long chat_id)
        {
            if (getChat(chat_id) == null)
            {
                Console.WriteLine("Creating data for chat " + chat_id.ToString());
                chat chatObj = new chat(chat_id);
                chatData.Add(chatObj);
                return chatObj;
            }
            else
            {
                throw new InvalidDataException("Chat already exists!");
            }
        }

    }

}
