using System;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Telegram.Bot;
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
            ExpectedReply e = new ExpectedReply(chatID, chatID, userName, text, isPM, null, null, replyToMessageID, false, null, markDown, clearKeyboard, false);

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
            Roboto.Settings.stats.logStat(new statItem("Outgoing Msgs", typeof(TelegramAPI)));

            if (caption.Length > 2000) { caption = caption.Substring(0, 1990); }

            Telegram.Bot.Types.ReplyParameters replyParameters = replyToMessageID != -1
                ? new Telegram.Bot.Types.ReplyParameters { MessageId = (int)replyToMessageID }
                : null;
            Telegram.Bot.Types.ReplyMarkups.ReplyMarkup replyMarkup = clearKeyboard
                ? new Telegram.Bot.Types.ReplyMarkups.ReplyKeyboardRemove()
                : null;

            try
            {
                Telegram.Bot.Types.Message sentMessage = TelegramAPI.Client.SendPhoto(
                    chatID,
                    Telegram.Bot.Types.InputFile.FromStream(image, fileName),
                    caption: caption,
                    replyParameters: replyParameters,
                    replyMarkup: replyMarkup
                ).GetAwaiter().GetResult();

                return sentMessage.MessageId;
            }
            catch (Exception e)
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
        public static long SendQuestion(long chatID, long userID, string text, bool isPrivateMessage, Type pluginType, string messageData, string userName = null, long replyToMessageID = -1, bool selective = false, List<List<string>> answerKeyboard = null, bool useMarkdown = false, bool clearKeyboard = false, bool trySendImmediately = false)
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
            // expectedReplies is one shared list touched by both the message thread and the phase-4
            // background scheduler thread - every direct read/mutation of it in this file is under
            // GlobalListsKey to keep the list structure itself safe (no more concurrent-modification
            // exceptions/corruption). This does NOT make the surrounding check-then-act queueing
            // logic (e.g. processNewExpectedReply below) fully atomic against a genuinely
            // concurrent add for the same user landing between a check and its decision - a real,
            // deliberately-accepted, narrow residual race, not solved by this pass. See
            // MIGRATION.md's phase 4 notes.
            using (ChatKeyedLock.Acquire(ChatKeyedLock.GlobalListsKey))
            {
                foreach (ExpectedReply e in Roboto.Settings.expectedReplies)
                {
                    if (e.userID == playerID) { return true; }
                }
                return false;
            }
        }

        /// <summary>
        /// Does the user have any outstanding (asked) expected Replies?
        /// </summary>
        /// <param name="playerID"></param>
        /// <returns></returns>
        public static bool userHasOutstandingQuestions(long playerID)
        {
            using (ChatKeyedLock.Acquire(ChatKeyedLock.GlobalListsKey))
            {
                foreach (ExpectedReply e in Roboto.Settings.expectedReplies)
                {
                    if (e.userID == playerID && e.isSent()) { return true; }
                }
                return false;
            }
        }


        /// <summary>
        /// Clear the expected Replies for a given plugin
        /// </summary>
        /// <param name="chat_id"></param>
        /// <param name="pluginType"></param>
        /// <param name="messageDataFilter">When non-empty, only clears replies whose messageData
        /// matches this - see the call from mod_xyzzy_chatdata.askQuestion for why this matters:
        /// without it, this clears *every* ExpectedReply for the plugin/chat regardless of what
        /// conversation it belongs to, including ones for a completely unrelated, still-outstanding
        /// flow for a different user (e.g. a queued /xyzzy_settings menu, sitting unsent behind that
        /// user's own in-progress answer) that just happens to share the same chat and plugin type.</param>
        public static void clearExpectedReplies(long chat_id, Type pluginType, string messageDataFilter = "")
        {
            using (ChatKeyedLock.Acquire(ChatKeyedLock.GlobalListsKey))
            {
                //find replies for this chat, and add them to a temp list
                List<ExpectedReply> repliesToRemove = new List<ExpectedReply>();
                foreach (ExpectedReply reply in Roboto.Settings.expectedReplies)
                {
                    if (reply.chatID == chat_id && reply.isOfType(pluginType)
                        && (messageDataFilter == "" || reply.messageData == messageDataFilter))
                    {
                        repliesToRemove.Add(reply);
                    }
                }
                //now remove them
                foreach (ExpectedReply reply in repliesToRemove)
                {
                    Roboto.Settings.expectedReplies.Remove(reply);
                    Roboto.log.log("Removed " + reply.text + " from expected replies", logging.loglevel.high);
                }
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
                // A group-targeted question (expectsReply=true, e.g. SendQuestion with
                // isPrivateMessage:false) was a real, confirmed-live bug in legacy: this branch
                // sends the message but - unlike both branches below - never adds `e` to
                // Roboto.Settings.expectedReplies, so parseExpectedReplies can never match a reply
                // to it. The reply is just silently lost; legacy's own "TODO - doesnt handle group
                // PMs" comment (still present in legacy-winforms-baseline) shows the original author
                // knew this path was incomplete. Confirmed via testing to have left mod_quote's
                // /quote_config (Set Duration + its retry) and mod_steam's /steam_addplayer
                // (including its very first prompt, not just the retry) completely non-functional -
                // every one of those was migrated to isPrivateMessage:true instead of teaching this
                // branch to also queue (see MIGRATION.md). Guarding here rather than silently fixing
                // it, since "group message expecting a reply" was never a working, exercised code
                // path in the first place - anything reaching this again is almost certainly the
                // same mistake, not a deliberate new use worth silently supporting.
                if (e.expectsReply)
                {
                    throw new NotImplementedException("SendQuestion/ExpectedReply with isPrivateMessage:false never actually registers the reply for matching (see this branch's own comment) - every real call site has been migrated to isPrivateMessage:true instead. Use that, or fix the underlying queueing gap here first.");
                }

                //send, dont queue.
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

                //queue if it was a question - lock scoped to just the Add, not sendMessage() above
                //(a real network call - never hold GlobalListsKey across one of those, it would
                //block every other chat's message processing for the call's whole duration).
                if (e.expectsReply)
                {
                    using (ChatKeyedLock.Acquire(ChatKeyedLock.GlobalListsKey)) { Roboto.Settings.expectedReplies.Add(e); }
                }
            }
            else
            {
                //chuck it on the queue
                using (ChatKeyedLock.Acquire(ChatKeyedLock.GlobalListsKey)) { Roboto.Settings.expectedReplies.Add(e); }
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
                //for each user, check if a message has been sent, and track the oldest message -
                //snapshot under the lock, but released before oldest.sendMessage() below (a real
                //network call) so it never blocks other chats' message processing.
                ExpectedReply oldest = null;
                bool sent = false;
                using (ChatKeyedLock.Acquire(ChatKeyedLock.GlobalListsKey))
                {
                    List<ExpectedReply> userReplies = Roboto.Settings.expectedReplies.Where(e => e.userID == userID).ToList();

                    //try find a message to send. Drop out if we already have a sent message on the stack (waiting for a reply)
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
                }

                //send the message if neccessary
                if (!sent && oldest != null)
                {
                    oldest.sendMessage();
                    if (!oldest.expectsReply)
                    {
                        using (ChatKeyedLock.Acquire(ChatKeyedLock.GlobalListsKey))
                        {
                            Roboto.Settings.expectedReplies.Remove(oldest);
                        }
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
            

            //main processing - all pure in-memory list cleanup, no network I/O, so the whole thing
            //(unlike trySendOutstandingMessagesForUser below, called per-user afterwards) can stay
            //under one lock acquisition without risking blocking another chat's message dispatch on
            //a slow network call.
            try
            {
                List<long> userIDs;
                using (ChatKeyedLock.Acquire(ChatKeyedLock.GlobalListsKey))
                {
                    Roboto.log.log("There are " + Roboto.Settings.expectedReplies.Count() + " expected replies on the stack", logging.loglevel.verbose);
                    Roboto.Settings.stats.logStat(new statItem("Expected Replies", typeof(mod_standard), Roboto.Settings.expectedReplies.Count()));

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
                    userIDs = Roboto.Settings.expectedReplies.Select(e => e.userID).Distinct().ToList<long>();

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
                }

                //outside the lock - trySendOutstandingMessagesForUser can call sendMessage() (real
                //network I/O) and manages its own locking internally per-call.
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
            using (ChatKeyedLock.Acquire(ChatKeyedLock.GlobalListsKey))
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
        }

        public static bool parseExpectedReplies(message m)
        {

            //are we expecteing this?
            bool processed = false;
            Modules.RobotoModuleTemplate pluginToCall = null;
            ExpectedReply er = null;
            try
            {
                using (ChatKeyedLock.Acquire(ChatKeyedLock.GlobalListsKey))
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

                    //remove here too (still under the same lock acquisition as the search above) -
                    //so a concurrent match on another thread for the same ExpectedReply genuinely
                    //can't happen, not just "unlikely".
                    if (processed && er != null) { Roboto.Settings.expectedReplies.Remove(er); }
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

            using (ChatKeyedLock.Acquire(ChatKeyedLock.GlobalListsKey)) { Roboto.Settings.expectedReplies.Remove(er); }
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
                // A failed send has no real incoming message to hand to the plugin - every
                // module's replyReceived override unconditionally dereferences fields off m (e.g.
                // m.text_msg.ToLower()) with no null check of its own, so passing null crashed the
                // whole main loop with a NullReferenceException the first time a send genuinely
                // failed (confirmed live: mod_quote's /quote_config flow, Telegram rejecting the
                // send with "message to be replied not found" - a stale reply-to target, an
                // occasional real-world case, not something fixable at the send layer). Confirmed
                // present byte-for-byte in legacy - passing null here is original design, just
                // never exercised until a send genuinely failed against a module that dereferences
                // m unconditionally. A minimal synthetic message built from the ExpectedReply's own
                // fields (chatID/userID/userName) keeps every module's existing dereferences safe
                // without auditing/fixing each one individually - string fields default to empty,
                // not null, specifically for this.
                message synthetic = new message
                {
                    chatID = er.chatID,
                    userID = er.userID,
                    userFullName = er.userName ?? "",
                };
                bool pluginProcessed = pluginToCall.replyReceived(er, synthetic, true);

                if (!pluginProcessed)
                {
                    // Not exceptional enough to crash over - a plugin not having a specific branch
                    // for "the message I wanted to send never arrived" is a soft, expected-to-
                    // sometimes-happen case, not a programming defect. This used to throw
                    // InvalidProgramException here, which - combined with the null-m crash above -
                    // meant a failed send was effectively guaranteed to take the main loop down.
                    Roboto.log.log("Plugin " + pluginToCall.GetType().ToString() + " didnt have a specific branch for a failed reply - ignoring.", logging.loglevel.normal);
                }
            }

        }

        public static void removeReply(ExpectedReply r)
        {
            using (ChatKeyedLock.Acquire(ChatKeyedLock.GlobalListsKey)) { Roboto.Settings.expectedReplies.Remove(r); }
        }

    }
}
