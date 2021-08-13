using System;
using System.Threading;
using System.Windows.Media;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Web;
using System.Text;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RobotoChatBot.Modules;

namespace RobotoChatBot
{
    public static class Messaging
    {
        //TODO - move ExpectedReplies here, migrate over, and load/save in separate file. 

        //TODO - handle multiple APIs, route based on chat type. 

        private static bool endLoop = false;

        public enum returnCodes { OK, Fatal, Unavail, Timeout };

        /// <summary>
        /// Quits any active update loops. 
        /// </summary>
        public static void quit()
        {
            //TODO - quit cleanly here
            endLoop = true;
        }

        /// <summary>
        /// Send a message. Returns the ID of the send message
        /// </summary>
        /// <param name="chatID">User or Chat ID</param>
        /// <param name="text"></param>
        /// <param name="markDown"></param>
        /// <param name="replyToMessageID"></param>
        /// <returns>An integer specifying the message id. -1 indicates it is queued, int.MinValue indicates a failure</returns>
        public static long SendMessage(long chatID, string text, string userName = null, bool markDown = false, long replyToMessageID = -1, bool clearKeyboard = false, bool trySendImmediately = false)
        {

            bool isPM = (chatID < 0 ? false : true);
            ExpectedReply e = new ExpectedReply(chatID, chatID, userName, text, isPM, null, null, replyToMessageID, false, "", markDown, clearKeyboard, false);

            //add the message to the stack. If it is sent, get the messageID back.
            long messageID = processNewExpectedReply(e, trySendImmediately);
            return messageID;

        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="chatID"></param>
        /// <param name="caption"></param>
        /// <param name="image"></param>
        /// <param name="fileName"></param>
        /// <param name="fileContentType"></param>
        /// <param name="replyToMessageID"></param>
        /// <param name="clearKeyboard"></param>
        /// <returns></returns>
        public static long SendPhoto(long chatID, string caption, Stream image, string fileName, string fileContentType, long replyToMessageID, bool clearKeyboard)
        {
            //TODO - should be cached in the expectedReply object first. 
            Roboto.Settings.stats.logStat(new statItem("Outgoing Msgs", typeof(TelegramAPI)));

            string postURL = Roboto.Settings.telegramAPIURL + Roboto.Settings.telegramAPIKey + "/sendPhoto";

            var pairs = new NameValueCollection();
            pairs["chat_id"] = chatID.ToString();
            pairs["caption"] = caption;

            if (caption.Length > 2000) { caption = caption.Substring(0, 1990); }
            if (replyToMessageID != -1) { pairs["reply_to_message_id"] = replyToMessageID.ToString(); }
            if (clearKeyboard) { pairs["reply_markup"] = "{\"hide_keyboard\":true}"; }
            try
            {
                JObject response = TelegramAPI.sendPOST(postURL, pairs, image, fileName, fileContentType).Result;
                //get the message ID
                int messageID = response.SelectToken("result.message_id").Value<int>();
                return messageID;
            }
            catch (WebException e)
            {
                //log it and carry on
                Roboto.log.log("Couldnt send photo " + fileName + " to " + chatID + "! " + e.ToString(), logging.loglevel.critical);
            }

            return -1;


        }

        /// <summary>
        /// Send a message, which we are expecting a reply to. Message can be sent publically or privately. Replies will be detected and sent via the plugin replyReceived method. 
        /// </summary>
        /// <param name="chatID">0 for a message not related to a specific chat - i.e. if the user is in a DM session with the bot</param>
        /// <param name="text"></param>
        /// <param name="replyToMessageID"></param>
        /// <param name="selective"></param>
        /// <param name="answerKeyboard"></param>
        /// <returns>An integer specifying the message id. -1 indicates it is queueed, long.MinValue indicates a failure</returns>
        public static long SendQuestion(long chatID, long userID, string text, bool isPrivateMessage, Type pluginType, string messageData, string userName = null, long replyToMessageID = -1, bool selective = false, string answerKeyboard = "", bool useMarkdown = false, bool clearKeyboard = false, bool trySendImmediately = false)
        {
            ExpectedReply e = new ExpectedReply(chatID, userID, userName, text, isPrivateMessage, pluginType, messageData, replyToMessageID, selective, answerKeyboard, useMarkdown, clearKeyboard, true);

            //add the message to the stack. If it is sent, get the messageID back.
            long messageID = processNewExpectedReply(e, trySendImmediately);
            return messageID;
        }


        /// <summary>
        /// Does the user have any outstanding (queued) expected Replies?
        /// </summary>
        /// <param name="playerID"></param>
        /// <returns></returns>
        public static bool userHasOutstandingMessages(long playerID)
        {
            foreach (ExpectedReply e in Roboto.Settings.expectedReplies)
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
        public static bool userHasOutstandingQuestions(long playerID)
        {
            foreach (ExpectedReply e in Roboto.Settings.expectedReplies)
            {
                if (e.userID == playerID && e.isSent()) { return true; }
            }
            return false;
        }


        /// <summary>
        /// Clear the expected Replies for a given plugin
        /// </summary>
        /// <param name="chat_id"></param>
        /// <param name="pluginType"></param>
        public static void clearExpectedReplies(long chat_id, Type pluginType)
        {
            //find replies for this chat, and add them to a temp list
            List<ExpectedReply> repliesToRemove = new List<ExpectedReply>();
            foreach (ExpectedReply reply in Roboto.Settings.expectedReplies)
            {
                if (reply.chatID == chat_id && reply.isOfType(pluginType)) { repliesToRemove.Add(reply); }
            }
            //now remove them
            foreach (ExpectedReply reply in repliesToRemove)
            {
                Roboto.Settings.expectedReplies.Remove(reply);
                Roboto.log.log("Removed " + reply.text + " from expected replies", logging.loglevel.high);
            }

        }

        public static void processUpdates()
        {

            DateTime lastUpdate = DateTime.MinValue;

            while (!endLoop)
            {
                //store the time to prevent hammering the service when its down. Pause for a couple of seconds if things are getting toasty
                lastUpdate = DateTime.Now;

                returnCodes code = TelegramAPI.getUpdates();

                if (code == returnCodes.Fatal)
                {
                    Roboto.log.log("Fatal Error when calling Telegram, exiting", logging.loglevel.critical);


                }
                 


                if (lastUpdate > DateTime.Now.Subtract(TimeSpan.FromSeconds(10)))
                {
                    Roboto.Settings.stats.logStat(new statItem("Hammering Prevention", typeof(Roboto)));
                    Roboto.log.log("Too quick, sleeping", logging.loglevel.warn);
                    Thread.Sleep(2000);
                }
                
            


            
            }

        }


        /// <summary>
        /// Add a new expected reply to the stack. Should be called internally only - New messages should be sent via TelegramAPI.GetExpectedReply
        /// </summary>
        /// <param name="e"></param>
        /// <param name="trySendImmediately">Try and send the message immediately, assuming nothing is outstanding. Will jump the queue, but not override any existing messages</param>
        /// <returns>An integer specifying the message id. -1 indicates it is queueed, long.MinValue indicates a failure</returns>
        private static long processNewExpectedReply(ExpectedReply e, bool trySendImmediately)
        {
            //flag the user as present in the chat
            if (e.isPrivateMessage)
            {
                Presence.markPresence(e.userID, e.chatID, e.userName);
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
            
            else if (
                //this is a PM. Does the user have anything actively asked that would block us from sending a message immediately?                
                (trySendImmediately && !userHasOutstandingQuestions(e.userID))
                ||
                //or for casual messages, is the queue empty
                !userHasOutstandingMessages(e.userID)
                )
            {
                //send the message.  
                messageID = e.sendMessage();

                if (messageID == long.MinValue)
                {
                    Roboto.log.log("Tried to send message, but it failed. trySendImmediately was " + trySendImmediately.ToString(), logging.loglevel.warn);
                    return messageID;
                }

                //queue if it was a question
                if (e.expectsReply) { Roboto.Settings.expectedReplies.Add(e); }
            }
            else
            {
                //chuck it on the queue
                Roboto.Settings.expectedReplies.Add(e);
            }

            //make sure we are in a safe state. This will make sure if we sent a message-only, that the next message(s) are processed. Potentially recursive.
            trySendOutstandingMessagesForUser(e.userID);

            return messageID;

        }


        /// <summary>
        /// Check if a user has any outstanding messages and try send one. 
        /// </summary>
        /// <param name="userID"></param>
        private static void trySendOutstandingMessagesForUser(long userID)
        {
            bool retry = true;
            while (retry)
            {
                //for each user, check if a message has been sent, and track the oldest message
                ExpectedReply oldest = null;
                List<ExpectedReply> userReplies = Roboto.Settings.expectedReplies.Where(e => e.userID == userID).ToList();

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
                        Roboto.Settings.expectedReplies.Remove(oldest);
                    }
                    //make sure we are in a safe state. This will make sure if we sent a message-only, that the next message(s) are processed. 
                }

                //what do we do next? 
                if (sent == true) { retry = false; } // drop out if we have a message awaiting an answer
                else if (oldest == null) { retry = false; } // drop out if we have no messages to send
                else if (oldest.expectsReply) { retry = false; } //drop out if we sent a message that expects a reply
            }
        }

        /// <summary>
        /// Do a healthcheck, and archive any old presence data
        /// Called from mod_standard's backgorund loop.
        /// </summary>
        public static void backgroundProcessing()
        {
            

            Roboto.log.log("There are " + Roboto.Settings.expectedReplies.Count() + " expected replies on the stack", logging.loglevel.verbose);
            Roboto.Settings.stats.logStat(new statItem("Expected Replies", typeof(mod_standard), Roboto.Settings.expectedReplies.Count()));

            //main processing
            try
            {
                //Remove any ERs that are for dead chats
                List<ExpectedReply> deadERs = new List<ExpectedReply>();
                foreach (ExpectedReply er in Roboto.Settings.expectedReplies)
                {
                    if (er.chatID != 0) //ignore messages that are specifically chat-less
                    {
                        chat c = Chats.getChat(er.chatID);
                        if (c == null) { deadERs.Add(er); }
                    }
                }
                foreach (ExpectedReply er in deadERs) { Roboto.Settings.expectedReplies.Remove(er); }
                Roboto.log.log("Removed " + deadERs.Count() + " dead expected replies, now " + Roboto.Settings.expectedReplies.Count() + " remain", deadERs.Count() == 0 ? logging.loglevel.verbose : logging.loglevel.warn);

                //remove any expired ones
                int i = Roboto.Settings.expectedReplies.RemoveAll(x => x.timeLogged < DateTime.Now.Subtract(TimeSpan.FromDays(Roboto.Settings.killInactiveChatsAfterXDays)));
                Roboto.log.log("Removed " + i + " expected replies, now " + Roboto.Settings.expectedReplies.Count() + " remain", i == 0 ? logging.loglevel.verbose : logging.loglevel.warn);

                //Build up a list of user IDs
                List<long> userIDs = Roboto.Settings.expectedReplies.Select(e => e.userID).Distinct().ToList<long>();

                //remove any invalid messages
                List<ExpectedReply> messagesToRemove = Roboto.Settings.expectedReplies.Where(e => e.outboundMessageID > 0 && e.expectsReply == false).ToList();
                if (messagesToRemove.Count > 0)
                {
                    Roboto.log.log("Removing " + messagesToRemove.Count() + " messages from queue as they are sent and dont require a reply", logging.loglevel.warn);
                }
                foreach (ExpectedReply e in messagesToRemove)
                {
                    Roboto.Settings.expectedReplies.Remove(e);
                }

                foreach (long userID in userIDs)
                {
                    trySendOutstandingMessagesForUser(userID);
                }
            }
            catch (Exception e)
            {
                Roboto.log.log("Error during expected reply housekeeping " + e.ToString(), logging.loglevel.critical);
            }

        }


        /// <summary>
        /// Get an array of expected replies for a given plugin
        /// </summary>
        /// <param name="chatID"></param>
        /// <param name="userID"></param>
        /// <param name="filter"></param>
        /// <returns></returns>
        public static List<ExpectedReply> getExpectedReplies(Type pluginType, long chatID, long userID = -1, string filter = "")
        {
            List<ExpectedReply> responses = new List<ExpectedReply>();
            foreach (ExpectedReply e in Roboto.Settings.expectedReplies)
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

        public static bool parseExpectedReplies(message m)
        {

            //are we expecteing this? 
            bool processed = false;
            Modules.RobotoModuleTemplate pluginToCall = null;
            ExpectedReply er = null;
            try
            {
                foreach (ExpectedReply e in Roboto.Settings.expectedReplies)
                {
                    //we are looking for direct messages from the user where c_id = m_id, OR reply messages where m_id = reply_id
                    //could trigger twice if we fucked something up - dont think this is an issue but checking processed flag for safety
                    if (!processed && e.isSent() && m.userID == e.userID)
                    {
                        if (m.chatID == e.userID || m.replyMessageID == e.outboundMessageID)
                        {
                            //find the plugin, send the expectedreply to it
                            foreach (Modules.RobotoModuleTemplate plugin in Plugins.plugins)
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
                Roboto.Settings.expectedReplies.Remove(er);

                try
                {
                    bool pluginProcessed = pluginToCall.replyReceived(er, m);

                    //reset our chat timer (if a successfully processed chat message)
                    if (pluginProcessed && er.chatID != 0)
                    {
                        chat c = Chats.getChat(er.chatID);
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

                //Do any follow up actions for this user. 
                Messaging.trySendOutstandingMessagesForUser(m.userID);

            }
            return processed;

        }


        /// <summary>
        /// Handle a failed outbound message that a plugin expects a reply for. 
        /// </summary>
        /// <param name="er"></param>
        public static void parseFailedReply(ExpectedReply er)
        {

            Roboto.Settings.expectedReplies.Remove(er);
            Modules.RobotoModuleTemplate pluginToCall = null;

            foreach (Modules.RobotoModuleTemplate plugin in Plugins.plugins)
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

        public static void removeReply(ExpectedReply r)
        {
            Roboto.Settings.expectedReplies.Remove(r);
        }

    }
}
