using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RobotoChatBot
{
    public static class Presence
    {
        //TODO - move RecentChatMembers here from settings, migrate & load/save to new file. 


        public static void backgroundProcessing()
        {


            Roboto.Settings.RecentChatMembers.RemoveAll(x => x.chatID == x.userID); //TODO <- this should be a startup housekeeping check only. 

            //Remove any stale presence info
            Roboto.Settings.RecentChatMembers.RemoveAll(x => x.lastSeen < DateTime.Now.Subtract(new TimeSpan(Roboto.Settings.chatPresenceExpiresAfterHours, 0, 0)));
        }

        /// <summary>
        /// Mark someone as having participated in a chat in some way. Used for determining wether to stamp outgoing messages or not, and for building up a recent picture of the chat members 
        /// </summary>
        /// <param name="userID"></param>
        /// <param name="chatID"></param>
        public static void markPresence(long userID, long chatID, string userName)
        {
            if (chatID < 0) //only mark group chats, not private chats. 
            {
                foreach (chatPresence p in Roboto.Settings.RecentChatMembers)
                {
                    if (p.userID == userID && p.chatID == chatID) { p.touch(userName); return; }
                }
                Roboto.Settings.RecentChatMembers.Add(new chatPresence(userID, chatID, userName));
            }
        }

        /// <summary>
        /// Gets a list of the chats that the user has been active in recently
        /// </summary>
        /// <param name="userID"></param>
        /// <returns></returns>
        public static List<chatPresence> getChatPresence(long userID)
        {
            return Roboto.Settings.RecentChatMembers.Where(x => x.userID == userID).ToList();

        }

        public static List<chatPresence> getChatRecentMembers(long chatID)
        {
            return Roboto.Settings.RecentChatMembers.Where(x => x.chatID == chatID).ToList();
        }



    }
}
