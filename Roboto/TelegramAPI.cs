using System;
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
        public static long SendMessage(long chatID, string text, bool markDown = false, long replyToMessageID = -1, bool clearKeyboard = false)
        {
            Roboto.Settings.stats.logStat(new statItem("Outgoing Msgs", typeof(TelegramAPI)));
            
            string postURL = Roboto.Settings.telegramAPIURL + Roboto.Settings.telegramAPIKey + "/sendMessage";

            var pairs = new NameValueCollection();
            pairs["chat_id"] = chatID.ToString();
            pairs["text"] =  text;
            
            if (text.Length > 1950 ) { text = text.Substring(0, 1950); }
            if (replyToMessageID != -1) { pairs["reply_to_message_id"] =replyToMessageID.ToString(); }
            if (markDown) { pairs["parse_mode"] = "Markdown"; }
            if (clearKeyboard) { pairs["reply_markup"] = "{\"hide_keyboard\":true}"; }
            try
            {
                JObject response = sendPOST(postURL, pairs).Result;
                
                //get the message ID
                int messageID = response.SelectToken("result.message_id").Value<int>();
                return messageID;
            }
            catch (WebException e)
            {
                //log it and carry on
                Roboto.log.log("Couldnt send message " + text + "to " + chatID + "! " + e.ToString(), logging.loglevel.critical);
            }
            
            return -1;
            
        }

        /// <summary>
        /// Send a message, which we are expecting a reply to.
        /// </summary>
        /// <param name="chatID"></param>
        /// <param name="text"></param>
        /// <param name="replyToMessageID"></param>
        /// <param name="selective"></param>
        /// <param name="answerKeyboard"></param>
        /// <returns></returns>
        [Obsolete ("Should call GetExpectedReply which will track responses properly")]
        public static long GetReply(long chatID, string text, long replyToMessageID = -1, bool selective = false, string answerKeyboard = "")
        {
            Roboto.Settings.stats.logStat(new statItem("Outgoing Msgs", typeof(TelegramAPI)));
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
                JObject response = sendPOST(postURL,pairs).Result;
                int messageID = response.SelectToken("result.message_id").Value<int>();
                return messageID;
            }
            catch (WebException e)
            {
                Console.WriteLine("Couldnt send message to " + chatID.ToString() + " because " + e.ToString());
                return -1;

            }
        }



        public static long SendPhoto(long chatID, string caption, Stream image, string fileName, string fileContentType, long replyToMessageID, bool clearKeyboard)
        {
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
                JObject response = sendPOST(postURL, pairs, image, fileName, fileContentType).Result;
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
        /// Send a message, which we are expecting a reply to. Message can be sent publically or privately. Replies will be detected and sent via the plugin replyRecieved method. 
        /// </summary>
        /// <param name="chatID"></param>
        /// <param name="text"></param>
        /// <param name="replyToMessageID"></param>
        /// <param name="selective"></param>
        /// <param name="answerKeyboard"></param>
        /// <returns></returns>
        public static long GetExpectedReply(long chatID, long userID, string text, bool isPrivateMessage, Type pluginType, string messageData, long replyToMessageID = -1, bool selective = false, string answerKeyboard = "")
        {
            ExpectedReply e = new ExpectedReply(chatID, userID, text, isPrivateMessage, pluginType, messageData, replyToMessageID, selective, answerKeyboard );
       
            //add the message to the stack. If it is sent, get the messageID back.
            long messageID = Roboto.Settings.newExpectedReply(e);
            return messageID;

        }

        /// <summary>
        /// Send the message in the expected reply. Should only be called from the expectedReply Class.
        /// </summary>
        /// <param name="e"></param>
        public static int postExpectedReplyToPlayer(ExpectedReply e)
        { 

            string postURL = Roboto.Settings.telegramAPIURL + Roboto.Settings.telegramAPIKey + "/sendMessage";

            var pairs = new NameValueCollection();
            string chatID = e.isPrivateMessage ? e.userID.ToString() : e.chatID.ToString(); //send to chat or privately
            pairs.Add("chat_id", chatID);
            pairs.Add("text", e.text);

            if (e.keyboard == "")
            {
                bool forceReply = !e.isPrivateMessage;

                //pairs.Add("reply_markup", "{\"force_reply\":true,\"selective\":" + e.selective.ToString().ToLower() + "}");
                pairs.Add("reply_markup", "{\"force_reply\":"
                    //force reply if we are NOT in a PM
                    + forceReply.ToString().ToLower()
                    //mark selective if passed in
                    +",\"selective\":" + e.selective.ToString().ToLower() + "}");
            }
            else
            {
                pairs.Add("reply_markup", "{" + e.keyboard + "}");
            }


            if (e.replyToMessageID != -1) { pairs.Add("reply_to_message_id", e.replyToMessageID.ToString()); }
            //TODO - should URLEncode the text.
            try
            {
                JObject response = sendPOST(postURL, pairs).Result;
                int messageID = response.SelectToken("result.message_id").Value<int>();
                return messageID;
            }
            catch (WebException ex)
            {
                Roboto.log.log("Couldnt send message to " + chatID.ToString() + " because " + ex.ToString(), logging.loglevel.high);
                
                //Mark as failed and return the failure to the calling method
                Roboto.log.log("Returning message " + e.messageData + " to plugin " + e.pluginType.ToString() + " as failed.", logging.loglevel.high);
                Roboto.Settings.parseFailedReply(e);
                
                return -1;

            }
        }

        /* Old version - uses querystring. Switched to multipart.
        /// <summary>
        /// Sends a POST message, returns the reply object
        /// </summary>
        /// <param name="postURL"></param>
        /// <returns></returns>
        public static JObject sendPOST(String postURL, NameValueCollection pairs, Byte[] image = null, string fileName = null, string fileContentType = null)
        {
            string finalString = postURL;
            Encoding enc = Encoding.GetEncoding(1252);
            WebClient client = new WebClient();
            
            //now move the params across
            bool first = true;

            foreach (string itemKey in pairs)
            {
                if (first)
                {
                    finalString += "?";
                    first = false;
                }
                else
                { finalString += "&"; }
                finalString += Uri.EscapeDataString(itemKey) + "=" + Uri.EscapeDataString(pairs[itemKey]);

            }
            

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(finalString);
            request.Method = "POST";
            
            Roboto.log.log("Sending Message:\n\r" + request.RequestUri.ToString(), logging.loglevel.low) ;

            if(image != null)
            {
                //boundarys for multipart messages
                string boundary = "----------" + DateTime.Now.Ticks.ToString("x");
                byte[] boundarybytes = Encoding.UTF8.GetBytes("--" + boundary + "\r\n");
                byte[] boundarytrailer = Encoding.UTF8.GetBytes("\r\n--" + boundary + "–-\r\n");
                string fileheaderTemplate = "Content-Dis-data; name=\"{0}\"; filename=\"{1}\";\r\nContent-Type: {2}\r\n\r\n";

                //set content type
                request.ContentType = "multipart/form-data; boundary=" + boundary;

                //send data on the stream
                Stream requestStream = request.GetRequestStream();
                WriteToStream(requestStream, boundarybytes);
                WriteToStream(requestStream, string.Format(fileheaderTemplate, "InputFile", fileName, fileContentType));
                WriteToStream(requestStream, image);
                WriteToStream(requestStream, boundarytrailer);
            }
            else
            {
                request.ContentType = "application/json";
            }

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
    */


        /// <summary>
        /// Sends a POST message, returns the reply object
        /// </summary>
        /// <param name="postURL"></param>
        /// <returns></returns>
        public static async System.Threading.Tasks.Task<JObject> sendPOST(String postURL, NameValueCollection pairs, Stream image = null, string fileName = null, string fileContentType = null)
        {

            var uri = new Uri(postURL);
            string logtxt = "";
            string responseObject = "";
            using (var client = new HttpClient())
            {

                try
                {
                    HttpResponseMessage response;


                    using (var form = new MultipartFormDataContent())
                    {
                        foreach (string itemKey in pairs)
                        {
                            form.Add(ConvertParameterValue(pairs[itemKey]), itemKey);
                            logtxt += itemKey + " = " + pairs[itemKey] + ". ";

                        }

                        if (image != null)
                        {
                            image.Seek(0, SeekOrigin.Begin);
                            HttpContent c = new StreamContent(image);
                            logtxt += "Image " + fileName + "added. ";
                            form.Add(c, "photo", fileName);
                        }

                        Roboto.log.log("Sending Message: " + postURL + "\n\r" + logtxt , logging.loglevel.low);

                        response = await client.PostAsync(uri, form).ConfigureAwait(false);

                    }
                    responseObject = await response.Content.ReadAsStringAsync();
                    
                }
                catch (HttpRequestException e) 
                {
                    Roboto.log.log("Unable to send Message due to HttpRequestException error:\n\r" + e.ToString(), logging.loglevel.high);
                }
                catch (Exception e)
                {
                    Roboto.log.log("Unable to send Message due to unknown error:\n\r" + e.ToString(), logging.loglevel.critical);
                }
                
                if (responseObject == null)
                    return null;

                JObject jo = JObject.Parse(responseObject);

                return jo;
            }




            /*string finalString = postURL;
            Encoding enc = Encoding.GetEncoding(1252);
            WebClient client = new WebClient();
            
            //boundarys for multipart messages
            string boundary = "----------" + DateTime.Now.Ticks.ToString("x");
            string boundarytrailer = boundary + "–-\r\n";
            string fileheaderTemplate = "Content-Disposition: form-data; name=\"{0}\"; filename=\"{1}\";\r\nContent-Type: {2}\r\n";
            string parameterTemplate  = "Content-Disposition: form-data; name=\"{0}\"\r\n{1}";

            //setup the request
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(finalString);
            request.Method = "POST";
            request.ContentType = "multipart/form-data; boundary=" + boundary;
            

            Roboto.log.log("Assembling Message:\n\r" + request.RequestUri.ToString(), logging.loglevel.verbose) ;
            
            
            bool first = true;

            using (Stream requestStream = request.GetRequestStream())
            {
                //now move the params across
                foreach (string itemKey in pairs)
                {
                    if (!first)
                    {
                        WriteToStream(requestStream, "\r\n");
                    }
                    WriteToStream(requestStream, boundary);
                    WriteToStream(requestStream, string.Format(parameterTemplate, itemKey, pairs[itemKey]));
                }
                //attach image
                if (image != null)
                {
                    WriteToStream(requestStream, "\r\n");
                    WriteToStream(requestStream, boundary);
                    WriteToStream(requestStream, string.Format(fileheaderTemplate, "InputFile", fileName, fileContentType));
                    WriteToStream(requestStream, image);
                }

                WriteToStream(requestStream, boundarytrailer);
                
            }
            

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
            */

            //UGGGGHHHH


        }


        private static HttpContent ConvertParameterValue(object value)
        {
            var type = value.GetType();

            switch (type.Name)
            {
                case "String":
                case "Int32":
                    return new StringContent(value.ToString());
                case "Boolean":
                    return new StringContent((bool)value ? "true" : "false");
                
                default:
                    var settings = new JsonSerializerSettings
                    {
                        DefaultValueHandling = DefaultValueHandling.Ignore,
                    };

                    return new StringContent(JsonConvert.SerializeObject(value, settings));
            }
        }

        /// <summary>
        /// Writes string to stream. Author : Farhan Ghumra
        /// http://stackoverflow.com/questions/19954287/how-to-upload-file-to-server-with-http-post-multipart-form-data
        /// </summary>
        private static void WriteToStream(Stream s, string txt )
        {
            Roboto.log.log( txt, logging.loglevel.verbose, ConsoleColor.White, false, false, false, true);
            byte[] bytes = Encoding.UTF8.GetBytes(txt);
            s.Write(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// Writes byte array to stream. Author : Farhan Ghumra
        /// http://stackoverflow.com/questions/19954287/how-to-upload-file-to-server-with-http-post-multipart-form-data
        /// </summary>
        private static void WriteToStream(Stream s, byte[] bytes)
        {
            s.Write(bytes, 0, bytes.Length);
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
