using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.IO;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace Roboto
{
    /// <summary>
    /// Methods that interact with the Telegram APIs
    /// </summary>
    public static class TelegramAPI
    {
        /// <summary>
        /// Send a message. Returns the ID of the send message
        /// </summary>
        /// <param name="chatID"></param>
        /// <param name="text"></param>
        /// <param name="markDown"></param>
        /// <param name="replyToMessageID"></param>
        /// <returns></returns>
        public static int SendMessage(int chatID, string text, bool markDown = false, int replyToMessageID = -1)
        {

            string postURL = Roboto.Settings.telegramAPIURL + Roboto.Settings.telegramAPIKey + "/sendMessage" +
                   "?chat_id=" + chatID +
                   "&text=" + text;
            if (replyToMessageID != -1) { postURL += "&reply_to_message_id=" + replyToMessageID; }
            if (markDown == true) { postURL += "&parse_mode=Markdown"; }

            JObject response = sendPOST(postURL);

            //get the message ID
            int messageID = response.SelectToken("result.message_id").Value<int>();
            return messageID;
        }


        public static int GetReply(int chatID, string text, int replyToMessageID = -1, bool selective = false, string answerKeyboard = "")
        {

            string postURL = Roboto.Settings.telegramAPIURL + Roboto.Settings.telegramAPIKey + "/sendMessage" +
                   "?chat_id=" + chatID +
                   "&text=" + text;

            if (answerKeyboard == "")
            {
                postURL += "&reply_markup={\"force_reply\":true,\"selective\":" + selective.ToString().ToLower() + "}";
            }
            else
            {
                postURL += "&reply_markup={" + answerKeyboard + "}";
            }


            if (replyToMessageID != -1) { postURL += "&reply_to_message_id=" + replyToMessageID; }
            //TODO - should URLEncode the text.
            try
            {
                JObject response = sendPOST(postURL);
                int messageID = response.SelectToken("result.message_id").Value<int>();
                return messageID;
            }
            catch (WebException e)
            {
                Console.WriteLine("Couldnt send message to " + chatID.ToString());
                return -1;

            }


            //get the message ID
            
        }

        /// <summary>
        /// Sends a POST message, returns the reply object
        /// </summary>
        /// <param name="postURL"></param>
        /// <returns></returns>
        public static JObject sendPOST(String postURL)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(postURL);

            request.Method = "POST";
            request.ContentType = "application/json";
            //request.ContentLength = DATA.Length;

            try
            {

                WebResponse webResponse = request.GetResponse();
                using (Stream webStream = webResponse.GetResponseStream())
                {
                    if (webStream != null)
                    {
                        using (StreamReader responseReader = new StreamReader(webStream))
                        {
                            string response = responseReader.ReadToEnd();

                            JObject jo = JObject.Parse(response);

                            //success?
                            string path = jo.First.Path;
                            string result = jo.First.First.Value<string>();


                            if (path != "ok" || result != "True")
                            {
                                Console.WriteLine("Error recieved sending message!");
                                //throw new WebException("Failure code from web service");

                            }

                            return jo;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                throw new WebException("Error during method call", e);
            }
            return null;
        }

        
        public static string createKeyboard(List<string> options, int width)
        {
            //["Answer1"],["Answer3"],["Answer12"],["Answer1"],["Answer3"],["Answer12"]
            string reply = "\"keyboard\":[";
            int column = 0;
            int pos = 0;
            foreach (String s in options)
            {
                //first element
                if (column == 0)
                {
                    reply += "[";
                }
                else
                {
                    reply += ",";
                }

                reply += "\"" + s + "\"";

                column++;
                //last element
                if (column == width && pos != options.Count - 1)
                {
                    column = 0;
                    reply += "],";
                }
                //very final element, 
                else if (pos == options.Count - 1)
                {
                    reply += "]";
                }

                pos++;
            }
            reply += "],\"one_time_keyboard\":true,\"resize_keyboard\":true";

            return reply;
        }


    }
}
