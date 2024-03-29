﻿using System;
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
using System.Text.RegularExpressions;

namespace RobotoChatBot
{
    /// <summary>
    /// Methods that interact with the Telegram APIs
    /// </summary>
    public static class TelegramAPI
    {
        

        /// <summary>
        /// Send the message in the expected reply. Should only be called from the expectedReply Class. May or may not expect a reply. 
        /// </summary>
        /// <param name="e"></param>
        /// <returns>A long specifying the message id. long.MinValue indicates a failure. Negative values are error codes</returns>
        public static long postExpectedReplyToPlayer(ExpectedReply e)
        {

            Roboto.Settings.stats.logStat(new statItem("Outgoing Msgs", typeof(TelegramAPI)));

            string postURL = Roboto.Settings.telegramAPIURL + Roboto.Settings.telegramAPIKey + "/sendMessage";

            //assemble collection of name/value data
            var pairs = new NameValueCollection();
            string chatID = e.isPrivateMessage ? e.userID.ToString() : e.chatID.ToString(); //send to chat or privately
            Roboto.log.log("Sending Message to " + chatID , logging.loglevel.low);
            try
            {
                pairs.Add("chat_id", chatID);
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
                pairs.Add("text", e.text);

                if (e.markDown) { pairs["parse_mode"] = "Markdown"; }

            }
            catch (Exception ex)
            {
                Roboto.log.log("Error assembling message!. " + ex.ToString(), logging.loglevel.critical);
            }
            try
            {
                //force a reply if we expect one, and the keyboard is empty
                if (e.expectsReply && (e.keyboard == null || e.keyboard == ""))

                {
                    bool forceReply = (!e.isPrivateMessage);

                    //pairs.Add("reply_markup", "{\"force_reply\":true,\"selective\":" + e.selective.ToString().ToLower() + "}");
                    pairs.Add("reply_markup", "{\"force_reply\":"
                        //force reply if we are NOT in a PM
                        + forceReply.ToString().ToLower()
                        //mark selective if passed in
                        + ",\"selective\":" + e.selective.ToString().ToLower() + "}");
                }

                else if (e.clearKeyboard) { pairs["reply_markup"] = "{\"hide_keyboard\":true}"; }
                else if (e.keyboard != null && e.keyboard != "")
                {
                    pairs.Add("reply_markup", "{" + e.keyboard + "}");
                }
                
            }
            catch (Exception ex)
            {
                //if we failed to attach, it probably wasnt important!
                Roboto.log.log("Error assembling message pairs. " + ex.ToString(), logging.loglevel.high);
            }
            try 
            {
                if (e.replyToMessageID != -1)
                {
                    pairs.Add("reply_to_message_id", e.replyToMessageID.ToString());
                }
            }
            catch (Exception ex)
            {
                //if we failed to attach, it probably wasnt important!
                Roboto.log.log("Error attaching Reply Message ID to message. " + ex.ToString() , logging.loglevel.high); 
            }

            try
            {
                JObject response = sendPOST(postURL, pairs).Result;

                if (response != null)
                {

                    bool success = response.SelectToken("ok").Value<Boolean>();
                    if (success)
                    {
                        
                        JToken response_token = response.SelectToken("result");
                        if (response_token != null)
                        {
                            JToken messageID_token = response.SelectToken("result.message_id");
                            if (messageID_token != null)
                            {
                                int messageID = messageID_token.Value<int>();
                                return messageID;
                            }
                            else { Roboto.log.log("MessageID Token was null.", logging.loglevel.high); }
                        }

                        else { Roboto.log.log("Response Token was null.", logging.loglevel.high); }
                    }
                    else
                    {
                        
                        int errorCode = response.SelectToken("error_code").Value<int>();
                        string errorDesc = response.SelectToken("description").Value<string>();

                        int result = parseErrorCode(errorCode, errorDesc);
                        Roboto.log.log("Message failed with code " + result, logging.loglevel.high);
                        Messaging.parseFailedReply(e);
                        return result;
                    }


                }
                else { Roboto.log.log("Response was null.", logging.loglevel.high); }

                Messaging.parseFailedReply(e);
                return long.MinValue;
            }
            catch (WebException ex)
            {
                Roboto.log.log("Couldnt send message to " + chatID.ToString() + " because " + ex.ToString(), logging.loglevel.high);

                //Mark as failed and return the failure to the calling method
                if (e.expectsReply)
                {
                    Roboto.log.log("Returning message " + e.messageData + " to plugin " + e.pluginType.ToString() + " as failed.", logging.loglevel.high);
                    Messaging.parseFailedReply(e);
                }
                return long.MinValue;
            }

            catch (Exception ex)
            {
                Roboto.log.log("Exception sending message to " + chatID.ToString() + " because " + ex.ToString(), logging.loglevel.high);

                //Mark as failed and return the failure to the calling method
                if (e.expectsReply)
                {
                    Roboto.log.log("Returning message " + e.messageData + " to plugin " + e.pluginType.ToString() + " as failed.", logging.loglevel.high);
                    Messaging.parseFailedReply(e);
                }
                return long.MinValue;

            }



        }

        public static Messaging.returnCodes getUpdates()
        {
            string updateURL = Roboto.Settings.telegramAPIURL + Roboto.Settings.telegramAPIKey + "/getUpdates" +
                "?offset=" + TelegramAPI.getUpdateID() +
                "&timeout=" + Roboto.Settings.waitDuration +
                "&limit=10";

            
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(updateURL);
            
            Roboto.log.log(".", logging.loglevel.low, Colors.White, true);
            request.Method = "GET";
            request.ContentType = "application/json";

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

                                Roboto.log.log("Failure code from web service", logging.loglevel.high);
                                return Messaging.returnCodes.Unavail;

                            }
                            else
                            {
                                int resultID = 0;
                                //open the response and parse it using JSON. Probably only one result, but should be more? 
                                foreach (JToken token in jo.SelectTokens("result.[*]"))//jo.Children()) //) records[*].data.importedPath"
                                {
                                    string logText = Regex.Replace(token.ToString(), @"(\s|\n|\r|\r\n)+", " ");
                                    Roboto.log.log(logText, logging.loglevel.verbose);

                                    //Find out what kind of message this is.

                                    //TOP LEVEL TOKENS
                                    JToken updateID_TK = token.First;
                                    JToken update_TK = updateID_TK.Next.First;

                                    //Flag the update ID as processed.
                                    int updateID = updateID_TK.First.Value<int>();
                                    Roboto.Settings.lastUpdate = updateID;

                                    //is this for a group chat?

                                    long chatID = update_TK.SelectToken("chat.id").Value<long>();

                                    chat chatData = null;
                                    if (chatID < 0)
                                    {
                                        //find the chat 
                                        chatData = Chats.getChat(chatID);
                                        string chatTitle = update_TK.SelectToken("chat.title").Value<string>();
                                        //new chat, add
                                        if (chatData == null)
                                        {
                                            chatData = Chats.addChat(chatID, chatTitle);
                                        }
                                        if (chatData == null)
                                        {
                                            throw new DataMisalignedException("Something went wrong creating the new chat data");
                                        }
                                        chatData.setTitle(chatTitle);
                                    }



                                    //Do we have an incoming message?
                                    if (update_TK.Path == "result[" + resultID.ToString() + "].message" && update_TK.SelectToken(".text") != null)
                                    {
                                        //prevent delays - its sent something valid back to us so we are probably OK. 

                                        if (chatData != null) { chatData.resetLastUpdateTime(); }

                                        message m = new message(update_TK);

                                        //now decide what to do with this stuff.
                                        bool processed = false;

                                        //check if this is an expected reply, and if so route it to the 
                                        Messaging.parseExpectedReplies(m);


                                        foreach (Modules.RobotoModuleTemplate plugin in Plugins.plugins)
                                        {

                                            //Skip this message if the chat is muted. 
                                            if (plugin.chatHook && (chatData == null || (chatData.muted == false || plugin.chatIfMuted)))
                                            {
                                                if ((!processed || plugin.chatEvenIfAlreadyMatched))
                                                {
                                                    processed = plugin.chatEvent(m, chatData);

                                                }
                                            }
                                        }

                                    }
                                    else
                                    {
                                        Roboto.log.log("No text in update", logging.loglevel.verbose);
                                    }
                                    //dont know what other update types we want to monitor? 
                                    //TODO - leave / kicked / chat deleted
                                    resultID++;
                                }
                            }
                        }
                    }
                }
            }
            catch (System.Net.WebException e)
            {
                Roboto.log.log("Web Service Timeout during getUpdates: " + e.ToString(), logging.loglevel.high);
                Roboto.Settings.stats.logStat(new statItem("BotAPI Timeouts", typeof(Roboto)));
                return Messaging.returnCodes.Timeout;
            }

            catch (Exception e)
            {
                Roboto.log.log("Exception caught at main loop. " + e.ToString(), logging.loglevel.critical, Colors.White, false, false, false, false, 2);
                return Messaging.returnCodes.Unavail;
            }
            return Messaging.returnCodes.OK;
        }

        public static int getUpdateID()
        {
            return Roboto.Settings.lastUpdate + 1;
        }


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

                        Roboto.log.log("Calling API: " + postURL + "\r\n" + logtxt, logging.loglevel.verbose);

                        response = await client.PostAsync(uri, form).ConfigureAwait(false);

                    }
                    responseObject = await response.Content.ReadAsStringAsync();
                    
                }
                catch (HttpRequestException e) 
                {
                    Roboto.log.log("Unable to send Message due to HttpRequestException error:\r\n" + e.ToString(), logging.loglevel.high);
                }
                catch (Exception e)
                {
                    Roboto.log.log("Unable to send Message due to unknown error:\r\n" + e.ToString(), logging.loglevel.critical);
                }

                if (responseObject == null || responseObject == "")
                {
                    Roboto.log.log("Sent message but received blank reply confirmation" , logging.loglevel.critical);
                    return null;
                }
                try
                {
                    Roboto.log.log("Result: " + responseObject, logging.loglevel.verbose);
                    JObject jo = JObject.Parse(responseObject);
                    if (jo != null)
                    {
                        return jo;
                    }
                    else
                    {
                        Roboto.log.log("JObject response object was null!", logging.loglevel.critical);
                        return null;
                    }
                }
                catch (Exception e)
                {
                    Roboto.log.log("Couldnt parse response from Telegram when sending message" + e.ToString(), logging.loglevel.critical);
                    Roboto.log.log("Response was: " + responseObject, logging.loglevel.critical);
                    return null;
                }
            }
            
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
            Roboto.log.log( txt, logging.loglevel.verbose, Colors.White, false, false, false, true);
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

        /// <summary>
        /// Checks the members of a group.
        /// </summary>
        /// <param name="chatID"></param>
        /// <returns>the member count. Will also return:
        /// -1 = failed to call
        /// </returns>
        public static int getChatMembersCount(long chatID)
        {
            string postURL = Roboto.Settings.telegramAPIURL + Roboto.Settings.telegramAPIKey + "/getChatMembersCount";

            var pairs = new NameValueCollection();
            pairs["chat_id"] = chatID.ToString();
           
            try
            {
                JObject response = sendPOST(postURL, pairs).Result;
                //get the message ID

                //TODO - move this error handling somewhere better & use elsewhere
                bool success = response.SelectToken("ok").Value<Boolean>();
                if (success)
                {
                    int memberCount = response.SelectToken("result").Value<int>();
                    return memberCount;
                }
                else
                {
                    
                    int errorCode = response.SelectToken("error_code").Value<int>();
                    string errorDesc = response.SelectToken("description").Value<string>();
                    return parseErrorCode(errorCode, errorDesc);

                }

            }
            catch (WebException e)
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
