using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Web;
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
        public static int SendMessage(int chatID, string text, bool markDown = false, int replyToMessageID = -1, bool clearKeyboard = false)
        {

            string postURL = Roboto.Settings.telegramAPIURL + Roboto.Settings.telegramAPIKey + "/sendMessage";

            var pairs = new NameValueCollection();
            pairs["chat_id"] = chatID.ToString();
            pairs["text"] =  text;
            
            
            if (replyToMessageID != -1) { pairs["reply_to_message_id"] =replyToMessageID.ToString(); }
            if (markDown) { pairs["parse_mode"] = "Markdown"; }
            if (clearKeyboard) { pairs["reply_markup"] = "\"hide_keyboard\":true}"; }

            JObject response = sendPOST(postURL, pairs);

            //get the message ID
            int messageID = response.SelectToken("result.message_id").Value<int>();
            return messageID;
        }


        public static int GetReply(int chatID, string text, int replyToMessageID = -1, bool selective = false, string answerKeyboard = "")
        {

            string postURL = Roboto.Settings.telegramAPIURL + Roboto.Settings.telegramAPIKey + "/sendMessage";

            var pairs = new NameValueCollection();
            pairs.Add("chat_id", chatID.ToString());
            pairs.Add("text", text);

            if (answerKeyboard == "")
            {
                pairs.Add("reply_markup","{\"force_reply\":true,\"selective\":" + selective.ToString().ToLower() + "}");
            }
            else
            {
                pairs.Add("reply_markup" ,"{" + answerKeyboard + "}");
            }


            if (replyToMessageID != -1) { pairs.Add("reply_to_message_id", replyToMessageID.ToString()); }
            //TODO - should URLEncode the text.
            try
            {
                JObject response = sendPOST(postURL,pairs);
                int messageID = response.SelectToken("result.message_id").Value<int>();
                return messageID;
            }
            catch (WebException e)
            {
                Console.WriteLine("Couldnt send message to " + chatID.ToString() + " because " + e.ToString());
                return -1;

            }


            //get the message ID
            
        }

        /// <summary>
        /// Sends a POST message, returns the reply object
        /// </summary>
        /// <param name="postURL"></param>
        /// <returns></returns>
        public static JObject sendPOST(String postURL, NameValueCollection pairs)
        {
            Encoding enc = Encoding.GetEncoding(1252);
            WebClient client = new WebClient();
            UriBuilder builder = new UriBuilder(postURL);
            //This is fucking stupid. PQS doesnt actually return a NVC, it returns some internal class, which I need an empty one of :/
            //This has the toString overridden so that it returns a URLEncoded querystring
            var pairsCollection = HttpUtility.ParseQueryString("http:\\example.com");
            //now move the params across
            foreach (string itemKey in pairs)
            {
                pairsCollection[itemKey] = pairs[itemKey];
            }
            builder.Query = pairsCollection.ToString();

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(builder.Uri);
            request.Method = "POST";
            request.ContentType = "application/json";
            Console.WriteLine("Sending Message:\n\r" + request.RequestUri.ToString());
            try
            {

                HttpWebResponse webResponse = (HttpWebResponse)request.GetResponse();

                if (webResponse != null)
                {
                    StreamReader responseSR = new StreamReader(webResponse.GetResponseStream(), enc);
                    string response = responseSR.ReadToEnd();

                    JObject jo = JObject.Parse(response);

                    //success?
                    string path = jo.First.Path;
                    string result = jo.First.First.Value<string>();


                    if (path != "ok" || result != "True")
                    {
                        Console.WriteLine("Error recieved sending message!");
                        //throw new WebException("Failure code from web service");

                    }
                    Console.WriteLine("Message Success");
                    return jo;

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
                //String s_clean = HttpUtility.HtmlEncode(s.Trim());
                String s_clean = JsonConvert.SerializeObject(s.Trim());

                //first element
                if (column == 0)
                {
                    reply += "[";
                }
                else
                {
                    reply += ",";
                }

                reply += s_clean;

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
