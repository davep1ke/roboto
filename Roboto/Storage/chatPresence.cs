using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RobotoChatBot
{
    /// <summary>
    /// Represents a user being part of a chat. Expires after a while. 
    /// </summary>
    public class chatPresence
    {
        public long userID;
        public long chatID;
        public string userName = "";
        public DateTime lastSeen = DateTime.Now;

        internal chatPresence() { }
        public chatPresence(long userID, long chatID, string userName)
        {
            this.userID = userID;
            this.chatID = chatID;
            this.userName = userName;
        }
        public void touch(string userName) { lastSeen = DateTime.Now; if (!string.IsNullOrEmpty(userName)) { this.userName = userName; } }

        public override string ToString()
        {
            return this.userName + "(" + this.userID + ")";
        }
    }
}
