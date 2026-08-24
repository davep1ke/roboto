using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RobotoChatBot
{
    public class ExpectedReply
    {
        /// <summary>
        /// The chat that the reply relates to (not the chat it was posted to, neccessarily).
        /// </summary>
        public long chatID = -1;
        public long userID = -1;
        public bool isPrivateMessage = false;
        public DateTime timeLogged = DateTime.Now;
        public DateTime timeSentToUser = DateTime.MinValue;
        public string text = "";
        public long replyToMessageID = -1;
        public bool selective = false;
        /// <summary>Null means "no keyboard" - was a raw JSON-fragment string before the
        /// Telegram.Bot port; now plain button-label rows (see TelegramAPI.createKeyboard's
        /// comment) rather than a real Telegram.Bot ReplyKeyboardMarkup, deliberately - this field
        /// is part of the persisted Roboto.Settings.expectedReplies graph (XmlSerializer can't
        /// serialize ReplyKeyboardMarkup itself, its Keyboard property is interface-typed). The real
        /// typed keyboard only gets built at the send boundary, in TelegramAPI.BuildReplyMarkup.
        /// TelegramAPI.postExpectedReplyToPlayer still treats null/clearKeyboard/force-reply as
        /// mutually exclusive the same way it always did.</summary>
        public List<List<string>> keyboard = null;
        public bool expectsReply = true;
        public bool markDown = false;
        public bool clearKeyboard = false;
        public string userName = "";

        /// <summary>
        /// Internal data that can be returned to the plugin after the response is received
        /// </summary>
        public string messageData;
        public string pluginType;
        public long outboundMessageID;

        /// <summary>SQLite row id once persisted, 0 = not yet written. Messaging.cs's
        /// addExpectedReply/removeExpectedReply and ExpectedReply.sendMessage() (below) use this to
        /// write through per-mutation instead of relying solely on the periodic full
        /// settings.save() flush - see SqliteStateStore.cs's expected_replies notes and
        /// MIGRATION.md's ER-durability addendum for why an unclean shutdown could otherwise lose
        /// in-flight conversational state.</summary>
        internal long dbId = 0;

        internal ExpectedReply() { }
        
        /// <summary>
        /// An outbound message that is logged on a stack, so that we can properly direct the reply, and send any further replies in sequence. 
        /// </summary>
        /// <param name="c"></param>
        /// <param name="userID"></param>
        /// <param name="text"></param>
        /// <param name="isPrivateMessage"></param>
        /// <param name="pluginType"></param>
        /// <param name="messageData"></param>
        public ExpectedReply(long chatID, long userID, string userName, string text, bool isPrivateMessage, Type pluginType, string messageData, long replyToMessageID, bool selective, List<List<string>> keyboard, bool  markDown, bool clearKeyboard, bool expectsReply)
        {
            
            this.chatID = chatID;
            this.userID = userID;
            this.userName = userName;
            this.text = text;
            this.isPrivateMessage = isPrivateMessage;
            this.messageData = messageData;
            if (pluginType != null)
            {
                this.pluginType = pluginType.ToString();
            }
            this.replyToMessageID = replyToMessageID;
            this.selective = selective;
            this.keyboard = keyboard;
            this.expectsReply = expectsReply;
            this.markDown = markDown;
            this.clearKeyboard = clearKeyboard;

        }

        /// <summary>
        /// Has the message been sent to the user?
        /// </summary>
        /// <returns></returns>
        public bool isSent()
        {
            if (timeSentToUser != DateTime.MinValue) { return true; }
            return false;
        }

        /// <summary>
        /// Check if this is of the right type. 
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public bool isOfType(Type t)
        {
            if (t.ToString() == pluginType)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Send the message
        /// </summary>
        /// <returns>An integer specifying the message id.long.MinValue indicates a failure</returns>
        public long sendMessage()
        {
            //lets restamp the users chat Presence (in case it took a long time on the queue)
            Presence.markPresence(userID, chatID, userName);
            outboundMessageID = TelegramAPI.postExpectedReplyToPlayer(this);

            timeSentToUser = DateTime.Now;

            // If this reply was queued (already persisted - see dbId's own comment) before being
            // sent, e.g. sitting behind another outstanding question, its row was written with no
            // outboundMessageID/timeSentToUser yet. Refresh those two columns now so a crash right
            // after send but before the next periodic settings.save() still matches an incoming
            // reply against the right outboundMessageID on restart, instead of silently reverting to
            // "never sent".
            if (dbId != 0) { Roboto.Store.UpdateExpectedReply(this); }

            return outboundMessageID;
        }
           

    }
}
