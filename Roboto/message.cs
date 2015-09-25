using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace Roboto
{
    /// <summary>
    /// An incoming chat message
    /// </summary>
    public class message
    {
        public int message_id;
        public int chatID;
        public String text_msg;
        public String userFirstName;
        public String userSurname;
        public String userFullName;
        public String chatName;
        public int userID = -1;

        //is this in reply to another text that we sent? 
        public bool isReply = false;
        public String replyOrigMessage = "";
        public String replyOrigUser = "";
        public int replyMessageID = -1;

        public message(JToken update_TK)
        {
            try
            {
                //get the message details
                message_id = update_TK.SelectToken(".message_id").Value<int>();
                chatID = update_TK.SelectToken(".chat.id").Value<int>();
                chatName = getNullableString(update_TK.SelectToken(".chat.title"));
                text_msg = update_TK.SelectToken(".text").Value<String>();
                userID = update_TK.SelectToken(".from.id").Value<int>();

                userFirstName = getNullableString(update_TK.SelectToken(".from.first_name"));
                userSurname = getNullableString(update_TK.SelectToken(".from.last_name"));
                userFullName = userFirstName + " " + userSurname;
                

                //in reply to...
                JToken replyMsg_TK = update_TK.SelectToken(".reply_to_message");
                if (replyMsg_TK != null)
                {
                    isReply = true;
                    replyOrigMessage = replyMsg_TK.SelectToken(".text").Value<String>();
                    replyOrigUser = replyMsg_TK.SelectToken(".from.username").Value<String>();
                    replyMessageID = replyMsg_TK.SelectToken(".message_id").Value<int>();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Error parsing message " + e.ToString());

            }

        }

        public string getNullableString(JToken token)
        {
            string rtn = "";

            if (token != null)
            {
                rtn = token.Value<String>();
            }

            return rtn;

        }
        



    }
}
