using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Media;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using RobotoChatBot.Modules;


namespace RobotoChatBot
{
    public static class Chats
    {
        //TODO - migrate Chats here from Settings, add load/save

        /// <summary>
        /// Check for dormant chats & plugins to purge
        /// </summary>
        public static void removeDormantChats()
        {
            logging.longOp lo_s = new logging.longOp("Dormant Chat Check", Roboto.Settings.chatData.Count());

            Roboto.log.log("Checking for Purgable chats / chat data", logging.loglevel.high, Colors.White, false, true);
            foreach (chat c in Roboto.Settings.chatData.Where(x => x.lastupdate < DateTime.Now.Subtract(new TimeSpan(Roboto.Settings.purgeInactiveChatsAfterXDays, 0, 0, 0))).ToList())
            {
                //check all plugins and remove data if no longer reqd
                bool isPurgable = c.tryPurgeData();

                //if all plugins are purged, delete the chat
                if (isPurgable)
                {
                    Roboto.log.log("Purging all data for chat " + c.chatID);
                    Roboto.Settings.stats.logStat(new statItem("Chats Purged", typeof(Roboto)));
                    Roboto.Settings.chatData.Remove(c);
                }
                else
                {
                    Roboto.log.log("Skipping purge of chat " + c.chatID + " as one or more plugins reported they shouldn't be purged");
                }
                lo_s.addone();
            }
            lo_s.complete();

        }



        /// <summary>
        /// find a chat by its chat ID
        /// </summary>
        /// <param name="chat_id"></param>
        /// <returns></returns>
        public static chat getChat(long chat_id)
        {
            foreach (chat c in Roboto.Settings.chatData)
            {
                if (c.chatID == chat_id)
                {
                    return c;
                }
            }
            return null;
        }

        /// <summary>
        /// Add data about a chat to the store. 
        /// </summary>
        /// <param name="chat_id"></param>
        public static chat addChat(long chat_id, string chatTitle)
        {
            if (getChat(chat_id) == null)
            {
                Console.WriteLine("Creating data for chat " + chat_id.ToString());
                chat chatObj = new chat(chat_id, chatTitle);
                Roboto.Settings.chatData.Add(chatObj);
                return chatObj;
            }
            else
            {
                throw new InvalidDataException("Chat already exists!");
            }
        }

    }
}

