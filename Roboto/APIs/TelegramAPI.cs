using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using RobotoChatBot.Modules;

namespace RobotoChatBot
{
    /// <summary>
    /// Methods that interact with the Telegram APIs.
    ///
    /// Ported off hand-rolled HttpWebRequest+JObject parsing onto the Telegram.Bot package - every
    /// method here keeps its exact original call signature (Messaging/ExpectedReply/every module
    /// call these the same way as before), only the implementation underneath changed. The one
    /// exception is createKeyboard's return type (string -> ReplyKeyboardMarkup, see its own
    /// comment) since a hand-built JSON fragment has no meaning to the typed client - every call
    /// site just passed that value straight through to Messaging.SendQuestion as an opaque value
    /// without inspecting it, so this was a mechanical propagation, not a behavior change.
    /// </summary>
    public static class TelegramAPI
    {
        private static ITelegramBotClient _client;

        /// <summary>Test-only hook: swaps in a fake ITelegramBotClient (Telegram.Bot's own
        /// SendMessage/GetUpdates/etc are extension methods on this interface, calling through to
        /// SendRequest - faking that one method covers everything built on top of it) so tests never
        /// make a real network call. Production code never calls this; Client's own getter lazily
        /// constructs the real TelegramBotClient exactly as before.</summary>
        internal static void SetClientForTesting(ITelegramBotClient client)
        {
            _client = client;
            _botId = null;
        }

        /// <summary>Lazily built, cached for the process lifetime - Roboto.Settings.telegramAPIKey
        /// is only ever set once at startup (from the XML config), never reassigned live, so there's
        /// no need to rebuild this per-call the way the old code recomputed the API URL string on
        /// every single request. Internal rather than private so Messaging.SendPhoto (the only other
        /// caller that needs the raw client, for the multipart photo upload) can reuse the same
        /// cached instance instead of constructing its own.</summary>
        internal static ITelegramBotClient Client => _client ??= new TelegramBotClient(Roboto.Settings.telegramAPIKey);

        private static long? _botId;

        /// <summary>Cached for the process lifetime same as Client itself - GetMe is a real API
        /// call, no reason to repeat it on every use (the bot self-de-admin background sweep,
        /// below, needs its own ID once per chat it checks). Reset alongside Client in
        /// SetClientForTesting so each test starts fresh rather than trusting a previous test's
        /// fake bot ID.</summary>
        internal static long BotId => _botId ??= Client.GetMe().GetAwaiter().GetResult().Id;

        /// <summary>
        /// Send the message in the expected reply. Should only be called from the expectedReply Class. May or may not expect a reply.
        /// </summary>
        /// <param name="e"></param>
        /// <returns>A long specifying the message id. long.MinValue indicates a failure</returns>
        public static long postExpectedReplyToPlayer(ExpectedReply e)
        {
            Roboto.Settings.stats.logStat(new statItem("Outgoing Msgs", typeof(TelegramAPI)));

            long chatId = e.isPrivateMessage ? e.userID : e.chatID; //send to chat or privately
            Roboto.log.log("Sending Message to " + chatId, logging.loglevel.low);

            try
            {
                if (e.text.Length > 1950) { e.text = e.text.Substring(0, 1950); }

                //check if the user has participated in multiple chats recently, so we can stamp the message with the current chat title.
                //only do this where the message relates to a chat. The chat ID shouldnt = the user id if this is the case.
                if (e.isPrivateMessage && e.chatID != e.userID && e.chatID < 0)
                {
                    int nrChats = Presence.getChatPresence(e.userID).Count();
                    if (nrChats > 1)
                    {
                        //get the current chat;
                        chat c = Chats.getChat(e.chatID);
                        if (c == null)
                        {
                            Roboto.log.log("Couldnt find chat for " + e.chatID + " - did you use the userID accidentally?", logging.loglevel.high);
                        }
                        else
                        {
                            if (e.markDown && c.chatTitle != null) { e.text = "*" + c.chatTitle + "* :" + "\r\n" + e.text; }
                            else { e.text = "=>" + c.chatTitle + "\r\n" + e.text; }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Roboto.log.log("Error assembling message!. " + ex.ToString(), logging.loglevel.critical);
            }

            ReplyMarkup replyMarkup = null;
            try
            {
                //force a reply if we expect one, and the keyboard is empty. Only actually in a group
                //chat (matches legacy's forceReply = !isPrivateMessage) - ForceReplyMarkup.ForceReply
                // is fixed true by the Telegram.Bot package (its mere presence in reply_markup means
                // "force a reply"), so the "false" case is simply "don't attach one at all", the same
                // real-world effect legacy's explicit force_reply:false had.
                if (e.expectsReply && e.keyboard == null && !e.isPrivateMessage)
                {
                    replyMarkup = new ForceReplyMarkup { Selective = e.selective };
                }
                else if (e.clearKeyboard) { replyMarkup = new ReplyKeyboardRemove(); }
                else if (e.keyboard != null) { replyMarkup = BuildReplyMarkup(e.keyboard); }
            }
            catch (Exception ex)
            {
                //if we failed to attach, it probably wasnt important!
                Roboto.log.log("Error assembling message pairs. " + ex.ToString(), logging.loglevel.high);
            }

            ReplyParameters replyParameters = null;
            try
            {
                if (e.replyToMessageID != -1)
                {
                    replyParameters = new ReplyParameters { MessageId = (int)e.replyToMessageID };
                }
            }
            catch (Exception ex)
            {
                //if we failed to attach, it probably wasnt important!
                Roboto.log.log("Error attaching Reply Message ID to message. " + ex.ToString(), logging.loglevel.high);
            }

            try
            {
                Message sentMessage = Client.SendMessage(
                    chatId,
                    e.text,
                    parseMode: e.markDown ? ParseMode.Markdown : ParseMode.None,
                    replyParameters: replyParameters,
                    replyMarkup: replyMarkup
                ).GetAwaiter().GetResult();

                return sentMessage.MessageId;
            }
            catch (ApiRequestException ex)
            {
                int result = parseErrorCode(ex.ErrorCode, ex.Message);
                Roboto.log.log("Message failed with code " + result, logging.loglevel.high);
                Messaging.parseFailedReply(e);
                return result;
            }
            catch (Exception ex)
            {
                Roboto.log.log("Exception sending message to " + chatId + " because " + ex.ToString(), logging.loglevel.high);

                //Mark as failed and return the failure to the calling method
                if (e.expectsReply)
                {
                    Roboto.log.log("Returning message " + e.messageData + " to plugin " + e.pluginType?.ToString() + " as failed.", logging.loglevel.high);
                    Messaging.parseFailedReply(e);
                }
                return long.MinValue;
            }
        }

        public static Messaging.returnCodes getUpdates()
        {
            Roboto.log.log(".", logging.loglevel.low, true);

            try
            {
                Update[] updates = Client.GetUpdates(
                    offset: TelegramAPI.getUpdateID(),
                    timeout: Roboto.Settings.waitDuration,
                    limit: 10
                ).GetAwaiter().GetResult();

                foreach (Update update in updates)
                {
                    DispatchUpdate(update);
                }
            }
            catch (ApiRequestException e)
            {
                Roboto.log.log("Failure code from web service: " + e.ErrorCode + " " + e.Message, logging.loglevel.high);
                return Messaging.returnCodes.Unavail;
            }
            catch (System.Net.Http.HttpRequestException e)
            {
                Roboto.log.log("Web Service Timeout during getUpdates: " + e.ToString(), logging.loglevel.high);
                Roboto.Settings.stats.logStat(new statItem("BotAPI Timeouts", typeof(Roboto)));
                return Messaging.returnCodes.Timeout;
            }
            catch (TaskCanceledException e)
            {
                Roboto.log.log("Web Service Timeout during getUpdates: " + e.ToString(), logging.loglevel.high);
                Roboto.Settings.stats.logStat(new statItem("BotAPI Timeouts", typeof(Roboto)));
                return Messaging.returnCodes.Timeout;
            }
            catch (Exception e)
            {
                Roboto.log.log("Exception caught at main loop. " + e.ToString(), logging.loglevel.critical, false, false, false, false, 2);
                return Messaging.returnCodes.Unavail;
            }
            return Messaging.returnCodes.OK;
        }

        public static int getUpdateID()
        {
            return Roboto.Settings.lastUpdate + 1;
        }

        /// <summary>
        /// The actual "what do we do with an incoming update" logic - pulled out of getUpdates()'s
        /// polling loop (phase 7) so it's directly callable/testable without a real network long-poll
        /// in the way: feed it a synthetic Update built by hand and assert on what the fake client
        /// recorded as "sent". No behavior change from the extraction itself - getUpdates() still
        /// calls this once per update it fetches, in the same order, under the same per-chat lock.
        /// </summary>
        /// <summary>Posted alongside the bot demoting itself back off admin - see DispatchUpdate's
        /// MyChatMember handling below (MIGRATION.md phase 9's "bot self-de-admin" delta).</summary>
        public const string BotSelfDeAdminExplanation =
            "Bots added as admin are sent every chat message within a group - I don't need to be an admin.";

        /// <summary>Sent instead of BotSelfDeAdminExplanation when the chat is a basic (non-super)
        /// group - see DeAdminSelf's own comment for why nothing can actually be stripped there.</summary>
        public const string BotSelfDeAdminBasicGroupExplanation =
            "Bots added as admin are sent every chat message within a group - I don't need to be an admin."
            + "Please remove my admin rights manually.";

        /// <summary>PromoteChatMember (the only "demote" mechanism the Bot API has - there's no
        /// separate "remove admin" call) only works for supergroups/channels. A basic (non-super)
        /// group has no per-member admin distinction at all via the Bot API, even though a human
        /// can still make the bot admin there through the Telegram app's own UI - found live: this
        /// used to be attempted unconditionally and crashed the whole update loop with an unhandled
        /// ApiRequestException ("400 Bad Request: method is available for supergroup and channel
        /// chats only") whenever that happened. Shared between the reactive MyChatMember handler
        /// below (which gets the chat's type for free off the update) and EnsureNotAdminInAnyChat's
        /// background sweep (which doesn't, and so discovers the same failure via the exception
        /// instead) - not duplicated per caller.</summary>
        private static void DeAdminSelf(long chatId, string chatTitle)
        {
            try
            {
                Client.PromoteChatMember(chatId, BotId).GetAwaiter().GetResult();
                Roboto.log.log("Promoted to admin in " + chatId + " (" + chatTitle + ") - de-admined self", logging.loglevel.high);
                Client.SendMessage(chatId, BotSelfDeAdminExplanation).GetAwaiter().GetResult();
            }
            catch (ApiRequestException ex) when (ex.Message.Contains("supergroup and channel", StringComparison.OrdinalIgnoreCase))
            {
                // Can never actually succeed here (see this method's own doc comment), so without
                // throttling, EnsureNotAdminInAnyChat's sweep would re-send this same explanation
                // every 5 minutes forever, as long as the bot stays admin in this chat - confirmed
                // live. Throttled to once a week - a gentle periodic reminder (user's explicit
                // call), not a one-time warning that's easy to miss, and not a constant nag either.
                mod_standard_chatdata chatData = Chats.getChat(chatId)?.getPluginData<mod_standard_chatdata>();
                if (chatData != null && chatData.lastBasicGroupAdminWarningDateTime.AddDays(7) > DateTime.Now)
                {
                    Roboto.log.log("Still admin in " + chatId + " (" + chatTitle + ") - a basic (non-super) group, warned within the last week already, not re-sending.", logging.loglevel.low);
                    return;
                }

                Roboto.log.log("Promoted to admin in " + chatId + " (" + chatTitle + "), but it's a basic (non-super) group - nothing to strip via the API. Explaining instead.", logging.loglevel.high);
                try
                {
                    Client.SendMessage(chatId, BotSelfDeAdminBasicGroupExplanation).GetAwaiter().GetResult();
                    if (chatData != null) { chatData.lastBasicGroupAdminWarningDateTime = DateTime.Now; }
                }
                catch (Exception ex2) { Roboto.log.log("Failed to send basic-group admin explanation to " + chatId + ": " + ex2.Message, logging.loglevel.high); }
            }
            catch (Exception ex)
            {
                Roboto.log.log("Failed to de-admin self in " + chatId + ": " + ex.Message, logging.loglevel.high);
            }
        }

        /// <summary>Safety-net sweep for mod_standard's backgroundProcessing() - checks every known
        /// chat's current membership status directly, rather than relying solely on the reactive
        /// MyChatMember event below (kept, not replaced - added alongside it per explicit request,
        /// in case a promotion's event was ever missed, e.g. a restart racing it). One GetChatMember
        /// call per known chat per pass - accepted cost of a real live check rather than trusting an
        /// event stream alone.</summary>
        public static void EnsureNotAdminInAnyChat()
        {
            foreach (chat c in Roboto.Settings.chatData.ToList())
            {
                try
                {
                    ChatMember member = Client.GetChatMember(c.chatID, BotId).GetAwaiter().GetResult();
                    if (member is ChatMemberAdministrator)
                    {
                        DeAdminSelf(c.chatID, c.chatTitle);
                    }
                }
                catch (Exception ex)
                {
                    Roboto.log.log("Couldn't check own admin status for chat " + c.chatID + ": " + ex.Message, logging.loglevel.low);
                }
            }
        }

        public static void DispatchUpdate(Update update)
        {
            //Flag the update ID as processed.
            Roboto.Settings.lastUpdate = update.Id;

            // MyChatMember fires whenever *this bot's own* membership status changes in a chat -
            // Telegram includes it in getUpdates()'s default update set with no explicit opt-in
            // needed (unlike chat_member, which covers *other* members and isn't included by
            // default). Not a legacy feature at all - legacy had no admin-only functionality and
            // never reacted to this update type. Carried forward from the abandoned rewrite
            // branch's own "bot self-de-admin" (MIGRATION.md phase 9): if someone promotes the bot
            // to admin, immediately strip every right back off - and explain why, so whoever
            // promoted it isn't left wondering what happened. Only reacts to a fresh promotion (old
            // status not already admin), not every no-op MyChatMember update.
            if (update.MyChatMember != null)
            {
                long chatId = update.MyChatMember.Chat.Id;

                // Register the chat here too, not just on the first real text message (below) -
                // found via a live gap while adding EnsureNotAdminInAnyChat's background sweep: a
                // chat where the bot is added and promoted straight to admin, with no other message
                // ever exchanged, was never in Roboto.Settings.chatData at all, so the sweep (which
                // only checks known chats) could never see it. MyChatMember fires for every
                // membership change (added, promoted, demoted, removed), so this is the right place
                // to make sure the bot always knows about every chat it's actually a member of.
                using (ChatKeyedLock.Acquire(chatId))
                {
                    if (Chats.getChat(chatId) == null)
                    {
                        Chats.addChat(chatId, update.MyChatMember.Chat.Title);
                    }
                }

                if (update.MyChatMember.NewChatMember.IsAdmin && !update.MyChatMember.OldChatMember.IsAdmin)
                {
                    DeAdminSelf(chatId, update.MyChatMember.Chat.Title);
                }
                return;
            }

            // Legacy generically resolved chat.id/chat.title off whatever the update's single
            // top-level payload object happened to be (it never explicitly requested non-message
            // update types, and its own TODO comment - "leave / kicked / chat deleted" - shows it
            // knew it didn't really handle them). The typed client separates update kinds into
            // distinct nullable properties instead of one generically-shaped token, so the
            // equivalent here is simply: only Update.Message carries a chat this bot ever acted on.
            if (update.Message == null || update.Message.Text == null)
            {
                Roboto.log.log("No text in update", logging.loglevel.verbose);
                return;
            }

            long chatID = update.Message.Chat.Id;

            // Locks this chat (or, for a private message, this user - chatID equals the user's own
            // ID for a 1:1 DM, and Telegram's own ID namespace guarantees the two never collide, see
            // ChatKeyedLock's comment) for the whole "process one incoming message" span - the same
            // chokepoint the phase-4 background scheduler's own per-chat work locks against, so the
            // two threads can never mutate the same chat's data at the same time.
            using (ChatKeyedLock.Acquire(chatID))
            {
                chat chatData = null;
                if (chatID < 0)
                {
                    //find the chat
                    chatData = Chats.getChat(chatID);
                    string chatTitle = update.Message.Chat.Title;
                    //new chat, add
                    if (chatData == null)
                    {
                        chatData = Chats.addChat(chatID, chatTitle);
                    }
                    if (chatData == null)
                    {
                        throw new InvalidOperationException("Something went wrong creating the new chat data");
                    }
                    chatData.setTitle(chatTitle);
                }

                //prevent delays - its sent something valid back to us so we are probably OK.
                if (chatData != null) { chatData.resetLastUpdateTime(); }

                message m = new message(update.Message);

                //now decide what to do with this stuff.
                bool processed = false;

                //check if this is an expected reply, and if so route it to the
                Messaging.parseExpectedReplies(m);

                foreach (Modules.RobotoModuleTemplate plugin in Plugins.plugins)
                {
                    //Skip this message if the chat is muted.
                    if (plugin.chatHook && (chatData == null || (chatData.muted == false || plugin.chatIfMuted)))
                    {
                        if (!processed || plugin.chatEvenIfAlreadyMatched)
                        {
                            processed = plugin.chatEvent(m, chatData);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Legacy built this as a raw JSON fragment string, hand-chunked into rows of `width`
        /// buttons and appended as-is to a bigger "reply_markup" JSON blob. Returns plain button-
        /// label rows here rather than a real Telegram.Bot ReplyKeyboardMarkup directly - see
        /// ExpectedReply.keyboard's comment for why (XmlSerializer can't serialize
        /// ReplyKeyboardMarkup's own IEnumerable-typed Keyboard property, and ExpectedReply.keyboard
        /// is itself part of the persisted Roboto.Settings.expectedReplies graph). Every call site
        /// just passed the old string result straight through to Messaging.SendQuestion without
        /// inspecting it, so this is still a mechanical propagation, not a behavior change - the
        /// actual ReplyKeyboardMarkup only gets built at the send boundary, in
        /// BuildReplyMarkup below.
        /// </summary>
        public static List<List<string>> createKeyboard(List<string> options, int width)
        {
            return options
                .Select(o => o.Trim())
                .Chunk(width)
                .Select(row => row.ToList())
                .ToList();
        }

        /// <summary>Builds the real Telegram.Bot keyboard from ExpectedReply.keyboard's plain
        /// button-label rows - kept separate from createKeyboard (above) precisely so the
        /// serializable, persisted shape and the send-time typed shape stay decoupled.</summary>
        private static ReplyKeyboardMarkup BuildReplyMarkup(List<List<string>> rows)
        {
            return new ReplyKeyboardMarkup(rows.Select(row => row.Select(label => new KeyboardButton(label)).ToList()).ToList())
            {
                OneTimeKeyboard = true,
                ResizeKeyboard = true
            };
        }

        /// <summary>
        /// Checks the members of a group.
        /// </summary>
        /// <param name="chatID"></param>
        /// <returns>the member count. Will also return:
        /// -1 = failed to call
        /// </returns>
        public static int getChatMembersCount(long chatID)
        {
            try
            {
                return Client.GetChatMemberCount(chatID).GetAwaiter().GetResult();
            }
            catch (ApiRequestException e)
            {
                return parseErrorCode(e.ErrorCode, e.Message);
            }
            catch (Exception e)
            {
                //log it and carry on
                Roboto.log.log("Couldnt get member count for " + chatID + "! " + e.ToString(), logging.loglevel.critical);
            }

            return -1;
        }

        /// <summary>
        /// Parse the error code / desc
        /// </summary>
        /// <param name="errorCode"></param>
        /// <param name="description"></param>
        /// <returns></returns>
        public static int parseErrorCode(int errorCode, string errorDesc)
        {



            List<string> errorDescs_403 = new List<string>()
                    {
                        "Forbidden: bot is not a member of the group chat",
                        "Forbidden: bot was kicked from the supergroup chat",
                        "Forbidden: bot was kicked from the group chat",
                        "Forbidden: bot was blocked by the user",
                        "Forbidden: Bot was blocked by the user",
                        "Bot was blocked by the user",
                        "Forbidden: bot can't initiate conversation with a user",
                        "Forbidden: Bot can't initiate conversation with a user",
                        "Bad Request: group chat was upgraded to a superground chat"
                    };

            List<string> errorDescs_400 = new List<string>()
                    {
                        "Bad Request: chat not found",
                        "Bad Request: group chat was migrated to a supergroup chat",
                        "PEER_ID_INVALID"
                    };


            //403 with a valid message:
            if (errorCode == 403 && errorDescs_403.Contains(errorDesc)) { return -403; }

            //Slightly less valid 403's (right message, wrong error code given)
            if (errorDescs_403.Contains(errorDesc)) { return -403; }

            //default 403 unmapped:
            if (errorCode == 403)
            {
                Roboto.log.log("Other Unmapped '403' error received - " + errorCode + " " + errorDesc + ". Assuming Forbidden", logging.loglevel.high);
                //return a -403 for this - we want to signal that the call failed
                return -403;
            }

            //400 with valid error - I see this as more of a 403 so suck it.
            if (errorCode == 400 && errorDescs_400.Contains(errorDesc)) { return -403; }

            //400 with valid error - I see this as more of a 403 so suck it.
            if (errorDescs_400.Contains(errorDesc)) { return -403; }

            //Catchall
            Roboto.log.log("Unmapped error received - " + errorCode + " - " + errorDesc, logging.loglevel.high);
            return -1;


        }
    }
}
