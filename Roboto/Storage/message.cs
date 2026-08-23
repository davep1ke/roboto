using System;

namespace RobotoChatBot
{
    /// <summary>
    /// An incoming chat message
    /// </summary>
    public class message
    {
        public long message_id;
        public long chatID;
        public String text_msg;
        public String userFirstName;
        public String userSurname;
        public String userFullName;
        public String userHandle = "";
        public String chatName;
        public long userID = -1;

        //is this in reply to another text that we sent?
        public bool isReply = false;
        public String replyOrigMessage = "";
        public String replyOrigUser = "";
        public long replyMessageID = -1;

        /// <summary>For Messaging.parseFailedReply's synthetic "the send itself failed, there's no
        /// real incoming message" case - see its own comment for why. text_msg/userFirstName/
        /// userSurname/userFullName/chatName have no field initializer (default null, unlike
        /// userHandle/replyOrigMessage/replyOrigUser above) - explicitly emptied here rather than
        /// left null, since every module's replyReceived unconditionally calls things like
        /// m.text_msg.ToLower() with no null check of its own.</summary>
        internal message()
        {
            text_msg = "";
            userFirstName = "";
            userSurname = "";
            userFullName = "";
            chatName = "";
        }

        /// <summary>Ported off hand-parsing a raw JToken (Newtonsoft) onto reading a typed
        /// Telegram.Bot.Types.Message directly - same field mapping, just via strongly-typed
        /// properties instead of SelectToken string paths.</summary>
        public message(Telegram.Bot.Types.Message tgMessage)
        {
            try
            {
                //get the message details
                message_id = tgMessage.MessageId;
                chatID = tgMessage.Chat.Id;
                chatName = tgMessage.Chat.Title ?? "";
                text_msg = tgMessage.Text;
                userID = tgMessage.From.Id;
                userHandle = tgMessage.From.Username ?? "";
                userFirstName = tgMessage.From.FirstName ?? "";
                userSurname = tgMessage.From.LastName ?? "";
                userFullName = userFirstName + " " + userSurname;

                //in reply to...
                if (tgMessage.ReplyToMessage != null)
                {
                    isReply = true;
                    replyOrigMessage = tgMessage.ReplyToMessage.Text ?? "";
                    replyOrigUser = tgMessage.ReplyToMessage.From?.Username ?? "";
                    replyMessageID = tgMessage.ReplyToMessage.MessageId;
                }
                Roboto.Settings.stats.logStat(new statItem("Incoming Msgs", typeof(TelegramAPI)));
                Roboto.log.log("Message:" + userFullName.PadRight(17, " "[0]) + " -> " + text_msg, logging.loglevel.low);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error parsing message " + e.ToString());

            }

        }

    }
}
