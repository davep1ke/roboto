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

            //TODO - temporary code from 2018 - can be removed. Replace any incorrect namespaces in the datafile. 
            foreach (ExpectedReply er in expectedReplies)
            {
                if (er.pluginType != null && er.pluginType.StartsWith("Roboto."))
                {
                    er.pluginType = "RobotoChatBot." + er.pluginType.Remove(0, 7);
                }
            }



            //TODO - all these checks should be general housekeeping and run on a schedule!!!
            logging.longOp lo_modules = new logging.longOp("Module Startup Checks", plugins.Count()*2);
            foreach(Modules.RobotoModuleTemplate plugin in plugins )
            {
                Roboto.log.log("Startup Checks for " + plugin.ToString(), logging.loglevel.warn);
                
                //moduledata and chatData startup checks
                Roboto.log.log("Checking chatdata for " + plugin.ToString(), logging.loglevel.warn);
                int i = chatData.Count();
                foreach (chat c in chatData)
                {
                    i--;
                    if (i % 100 == 0) { Roboto.log.log(i.ToString() + " remaining", logging.loglevel.verbose); }
                    c.initPlugins();
                    if (plugin.pluginChatDataType != null)
                    {
                        RobotoModuleChatDataTemplate cd = c.getPluginData(plugin.pluginChatDataType);
                        if (cd != null) { cd.startupChecks(); }
                    }
                }
                Roboto.log.log("Checking coredata for " + plugin.ToString(), logging.loglevel.warn);
                plugin.getPluginData().startupChecks();
                lo_modules.addone();
                Roboto.log.log("Checking module for " + plugin.ToString(), logging.loglevel.warn);
                plugin.startupChecks();
                lo_modules.addone();
               
            }
            lo_modules.complete();
            
        }


        /// <summary>
        /// Basic checks on the data. 
        /// </summary>
        public void validate()
        {
            stats.startup();
            foreach (Modules.RobotoModuleTemplate plugin in plugins)
            {
                plugin.initData(); //this data probably already exists if loaded by XML, but if not, allow the plugin to create it. 
                /*if (plugin.pluginDataType != null)
                {
                    //TODO - check if this datatype is a subclass of RobotoModuleDataTemplate
                }*/
            }


            if (telegramAPIURL == null) {telegramAPIURL = "https://api.telegram.org/bot";};
            if (telegramAPIKey == null) { telegramAPIKey = "ENTERYOURAPIKEYHERE"; };
            if (botUserName == "") { botUserName = "Roboto_bot_name"; }


            Roboto.log.log("All Plugins initialised", logging.loglevel.high, Colors.White, false, true);
            Roboto.log.log((Modules.mod_standard.getAllMethodDescriptions()));

           

            
            //Check for dormant chats & plugins to purge
            //TODO - move this to a background proc.

            Roboto.log.log("Checking for Purgable chats / chat data", logging.loglevel.high, Colors.White, false, true);
            foreach (chat c in chatData.Where(x => x.lastupdate < DateTime.Now.Subtract(new TimeSpan(purgeInactiveChatsAfterXDays,0,0,0))).ToList())
            {
                //check all plugins and remove data if no longer reqd
                c.tryPurgeData();

                //if all plugins are purged, delete the chat
                if (c.isPurgable())
                {
                    Roboto.log.log("Purging all data for chat " + c.chatID);
                    Roboto.Settings.stats.logStat(new statItem("Chats Purged", typeof(Roboto)));
                    chatData.Remove(c);
                }
                else
                {
                    Roboto.log.log("Skipping purge of chat " + c.chatID + " as one or more plugins reported they shouldn't be purged");
                }
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
                    //todo - if the XML is bad, it will trigger here. Need a way to display to the user. 
                    Console.WriteLine(e.ToString());
                }
            }
            return null;

        }

        /// <summary>
        /// Does the user have any outstanding (queued) expected Replies?
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
        /// Does the user have any outstanding (asked) expected Replies?
        /// </summary>
        /// <param name="playerID"></param>
        /// <returns></returns>
        public bool userHasOutstandingQuestions(long playerID)
        {
            foreach (ExpectedReply e in expectedReplies)
            {
                if (e.userID == playerID && e.isSent()) { return true; }
            }
            return false;
        }

        /// <summary>
        /// Mark someone as having participated in a chat in some way. Used for determining wether to stamp outgoing messages or not, and for building up a recent picture of the chat members 
        /// </summary>
        /// <param name="userID"></param>
        /// <param name="chatID"></param>
        public void markPresence(long userID, long chatID, string userName)
        {
            if (chatID < 0) //only mark group chats, not private chats. 
            {
                foreach (chatPresence p in RecentChatMembers)
                {
                    if (p.userID == userID && p.chatID == chatID) { p.touch(userName); return; }
                }
                RecentChatMembers.Add(new chatPresence(userID, chatID, userName));
            }
        }

        /// <summary>
        /// Gets a list of the chats that the user has been active in recently
        /// </summary>
        /// <param name="userID"></param>
        /// <returns></returns>
        public List<chatPresence> getChatPresence(long userID)
        {
            return RecentChatMembers.Where(x => x.userID == userID).ToList();

        }

        public List<chatPresence> getChatRecentMembers(long chatID)
        {
            return RecentChatMembers.Where(x => x.chatID == chatID).ToList();
        }


        /// <summary>
        /// Add a new expected reply to the stack. Should be called internally only - New messages should be sent via TelegramAPI.GetExpectedReply
        /// </summary>
        /// <param name="e"></param>
        /// <param name="trySendImmediately">Try and send the message immediately, assuming nothing is outstanding. Will jump the queue, but not override any existing messages</param>
        /// <returns>An integer specifying the message id. -1 indicates it is queueed, long.MinValue indicates a failure</returns>
        public long newExpectedReply(ExpectedReply e, bool trySendImmediately)
        {
            //flag the user as present in the chat
            if (e.isPrivateMessage)
            {
                markPresence(e.userID, e.chatID, e.userName);
            }

            //check if we can send it? Get the messageID back
            long messageID = -1;
            //is this a message to a group? 
            if (!e.isPrivateMessage)
            {
                //send, dont queue.
                //TODO - doesnt handle group PMs
                messageID = e.sendMessage();
            }
            //this is a PM. Does the user have anything in the queue?                
            else if (!userHasOutstandingMessages(e.userID))
            {
                //send the message.  
                messageID = e.sendMessage();
                //queue if it was a question
                if (e.expectsReply) { expectedReplies.Add(e); }
            }
            //If they have messages in the queue, and we want to jump ahead, has one already been asked, or are we open? 
            else if (trySendImmediately && !userHasOutstandingQuestions(e.userID))
            {
                Roboto.log.log("Message jumping queue due to immediatemode", logging.loglevel.verbose);
                //send the message, grab the ID. 
                messageID = e.sendMessage();
                //did it work/
                if (messageID == long.MinValue)
                {
                    Roboto.log.log("Tried to send message using immediateMode, but it failed.", logging.loglevel.warn);
                    return messageID;
                }

                //queue if it was a question
                else if (e.expectsReply) { expectedReplies.Add(e); }
            }
            else
            {
                //chuck it on the stack if its going to be queued
                expectedReplies.Add(e);
            }

            //make sure we are in a safe state. This will make sure if we sent a message-only, that the next message(s) are processed. 
            expectedReplyBackgroundProcessing();

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
                Roboto.log.log("Removed " + reply.text + " from expected replies", logging.loglevel.high);
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

        public static RobotoModuleTemplate getPlugin(Type type)
        {
            foreach(RobotoModuleTemplate t in plugins)
            {
                if (t.GetType() == type)
                {
                    return t;
                }
            }

            return null;
            
        }


        /// <summary>
        /// Do a healthcheck, and archive any old presence data
        /// Called from mod_standard's backgorund loop.
        /// </summary>
        public void expectedReplyBackgroundProcessing()
        {
            

            RecentChatMembers.RemoveAll(x => x.chatID == x.userID); //TODO <- this should be a startup housekeeping check only. 

            //TODO - are we calling this whole thing every loop at the moment? Move to mod_standard.background?  
            //Remove any stale presence info
            RecentChatMembers.RemoveAll(x => x.lastSeen < DateTime.Now.Subtract(new TimeSpan(chatPresenceExpiresAfterHours, 0, 0)));
            
            Roboto.log.log("There are " + expectedReplies.Count() + " expected replies on the stack", logging.loglevel.verbose);

            //main processing
            try
            {
                //Remove any ERs that are for dead chats
                List<ExpectedReply> deadERs = new List<ExpectedReply>();
                foreach (ExpectedReply er in expectedReplies)
                {
                    if (er.chatID != 0) //ignore messages that are specifically chat-less
                    {
                        chat c = getChat(er.chatID);
                        if (c == null) { deadERs.Add(er); }
                    }
                }
                foreach (ExpectedReply er in deadERs) { expectedReplies.Remove(er); }
                Roboto.log.log("Removed " + deadERs.Count() + " dead expected replies, now " + expectedReplies.Count() + " remain", deadERs.Count() == 0 ? logging.loglevel.verbose : logging.loglevel.warn);

                //remove any expired ones
                int i = expectedReplies.RemoveAll(x => x.timeLogged < DateTime.Now.Subtract(TimeSpan.FromDays(Roboto.Settings.killInactiveChatsAfterXDays)));
                Roboto.log.log("Removed " + i + " expected replies, now " + expectedReplies.Count() + " remain", i == 0 ? logging.loglevel.verbose : logging.loglevel.warn);

                //Build up a list of user IDs
                List<long> userIDs = expectedReplies.Select(e => e.userID).Distinct().ToList<long>();

                //remove any invalid messages
                List<ExpectedReply> messagesToRemove = expectedReplies.Where(e => e.outboundMessageID > 0 && e.expectsReply == false).ToList();
                if (messagesToRemove.Count > 0)
                {
                    Roboto.log.log("Removing " + messagesToRemove.Count() + " messages from queue as they are sent and dont require a reply", logging.loglevel.warn);
                }
                foreach (ExpectedReply e in messagesToRemove)
                {
                    expectedReplies.Remove(e);
                }

                foreach (long userID in userIDs)
                {
                    bool retry = true;
                    while (retry)
                    {
                        //for each user, check if a message has been sent, and track the oldest message
                        ExpectedReply oldest = null;
                        List<ExpectedReply> userReplies = expectedReplies.Where(e => e.userID == userID).ToList();

                        //try find a message to send. Drop out if we already have a sent message on the stack (waiting for a reply)
                        bool sent = false;
                        foreach (ExpectedReply e in userReplies)
                        {
                            if (e.isSent()) { sent = true; } //message is waiting
                            else
                            {
                                if (oldest == null || e.timeLogged < oldest.timeLogged)
                                {
                                    oldest = e;
                                }
                            }
                        }

                        //send the message if neccessary
                        if (!sent && oldest != null)
                        {
                            oldest.sendMessage();
                            if (!oldest.expectsReply)
                            {
                                expectedReplies.Remove(oldest);

                            }
                            //make sure we are in a safe state. This will make sure if we sent a message-only, that the next message(s) are processed. 

                        }

                        //what do we do next? 
                        if (sent == true) { retry = false; } // drop out if we have a message awaiting an answer
                        else if (oldest == null) { retry = false; } // drop out if we have no messages to send
                        else if (oldest.expectsReply) { retry = false; } //drop out if we sent a message that expects a reply

                    }
                }
            }
            catch (Exception e)
            {
                Roboto.log.log("Error during expected reply housekeeping " + e.ToString(), logging.loglevel.critical);
            }
        }

        /// <summary>
        /// General background processing loop. Called 
        /// </summary>
        public void backgroundProcessing(bool force)
        {
            foreach (Modules.RobotoModuleTemplate plugin in plugins)
            {
                if (plugin.backgroundHook)
                {
                    try
                    {
                        plugin.callBackgroundProcessing(force);
                    }
                    catch (Exception e)
                    {
                        Console.Out.WriteLine("-----------------");
                        Console.Out.WriteLine("Error During Plugin " + plugin.GetType().ToString() + " background processing");
                        Console.Out.WriteLine(e.Message);
                    }
                }
            }
        }

        /*// <summary>
        /// Make sure any reply processing is being done
        /// </summary>
        public void expectedReplyHousekeeping()
        {

            
        }*/

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
            try
            {
                foreach (ExpectedReply e in expectedReplies)
                {
                    //we are looking for direct messages from the user where c_id = m_id, OR reply messages where m_id = reply_id
                    //could trigger twice if we fucked something up - dont think this is an issue but checking processed flag for safety
                    if (!processed && e.isSent() && m.userID == e.userID)
                    {
                        if (m.chatID == e.userID || m.replyMessageID == e.outboundMessageID)
                        {
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
                            processed = true;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Roboto.log.log("Error matching incoming message to plugin - " + e.ToString(), logging.loglevel.critical);
            }


            if (processed)
            {
                if (er == null)
                {
                    Roboto.log.log("Expected reply found, but er not available.", logging.loglevel.critical);
                    return true;
                }
                if (pluginToCall == null)
                {
                    Roboto.log.log("Expected reply plugin found, but not available.", logging.loglevel.critical);
                    return true;
                }

                //now send it to the plugin (remove first, so any checks can be done)
                expectedReplies.Remove(er);
                
                try
                {
                    bool pluginProcessed = pluginToCall.replyReceived(er, m);

                    //reset our chat timer (if a successfully processed chat message)
                    if (pluginProcessed && er.chatID != 0)
                    {
                        chat c = getChat(er.chatID);
                        if (c != null) { c.resetLastUpdateTime(); }
                        else { Roboto.log.log("Chat not found for update.", logging.loglevel.high); }
                    }
                    else if (er.chatID == 0)
                    {
                        Roboto.log.log("No chat - skipping update of chat timers", logging.loglevel.verbose);
                    }
                    else
                    {
                        throw new InvalidProgramException("Plugin didnt process the message it expected a reply to!");
                    }
                }
                catch (Exception e)
                {
                    Roboto.log.log("Error calling plugin " + pluginToCall.GetType().ToString() + " with expected reply. " + e.ToString(), logging.loglevel.critical);
                }
                
                //Do any follow up er actions. 
                expectedReplyBackgroundProcessing();
                
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
            if (pluginToCall == null)
            {
                Roboto.log.log("Expected Reply wasnt on the stack - probably sent in immediate-mode! Couldnt remove it", logging.loglevel.normal);
            }
            else
            {
                bool pluginProcessed = pluginToCall.replyReceived(er, null, true);

                if (!pluginProcessed)
                {
                    Roboto.log.log("Plugin " + pluginToCall.GetType().ToString() + " didnt process the message it expected a reply to!", logging.loglevel.high);
                    throw new InvalidProgramException("Plugin didnt process the message it expected a reply to!");

                }
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
            
            //use datepart to keep a file for each day. 
            //TODO - tidy up files older than x days. 
            string datePart = DateTime.Now.ToString("yyyy-MM-dd") + ".xml";

            //delete our old backup
            FileInfo fi = new FileInfo(filename + "." + datePart);
            if (fi.Exists) { fi.Delete(); }

            //replace our current backup
            FileInfo fi_backup = new FileInfo(filename);
            if (fi_backup.Exists) { fi_backup.MoveTo(filename + "." + datePart); }

            
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
        public chat addChat(long chat_id, string chatTitle)
        {
            if (getChat(chat_id) == null)
            {
                Console.WriteLine("Creating data for chat " + chat_id.ToString());
                chat chatObj = new chat(chat_id, chatTitle);
                chatData.Add(chatObj);
                return chatObj;
            }
            else
            {
                throw new InvalidDataException("Chat already exists!");
            }
        }

    }

    /// <summary>
    /// Represents a user being part of a chat. Expires after a while. 
    /// </summary>
    public class chatPresence
    {
        public long userID;
        public long chatID;
        public string userName = "";
        public DateTime lastSeen = DateTime.Now;

        internal chatPresence() { }
        public chatPresence(long userID, long chatID, string userName)
        {
            this.userID = userID;
            this.chatID = chatID;
            this.userName = userName;
        }
        public void touch(string userName) { lastSeen = DateTime.Now; if (!string.IsNullOrEmpty(userName)){ this.userName = userName; } }

        public override string ToString()
        {
            return this.userName + "(" + this.userID + ")";
        }
    }
}
