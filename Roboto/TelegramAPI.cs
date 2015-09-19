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
        public static void SendMessage(int chatID, string text, bool markDown = false, int replyToMessageID = -1)
        {

            string postURL = Roboto.Settings.telegramAPIURL + Roboto.Settings.telegramAPIKey + "/sendMessage" +
                   "?chat_id=" + chatID +
                   "&text=" + text;
            if (replyToMessageID != -1) { postURL += "&reply_to_message_id=" + replyToMessageID; }
            if (markDown == true) { postURL += "&parse_mode=Markdown"; }

            sendPOST(postURL);

        }


        public static void GetReply(int chatID, string text, int replyToMessageID = -1, bool selective = false)
        {

            string postURL = Roboto.Settings.telegramAPIURL + Roboto.Settings.telegramAPIKey + "/sendMessage" +
                   "?chat_id=" + chatID +
                   "&text=" + text +
                   "&reply_markup={\"force_reply\":true,\"selective\":" + selective.ToString().ToLower() + "}";

            if (replyToMessageID != -1) { postURL += "&reply_to_message_id=" + replyToMessageID; }
            //TODO - should URLEncode the text.
            sendPOST(postURL);

        }
        public static void sendPOST(String postURL)
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
                        }
                    }
                }
            }
            catch (Exception e)
            {
                throw new WebException("Error during method call", e);
            }
        }
    }
}
