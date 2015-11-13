using System;
using System.Collections.Generic;

using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace Roboto.Modules
{

    /// <summary>
    /// Core Data to be stored in the XML store. NEeded to hold the "last update date" for backgorund processing
    /// </summary>
    [XmlType("mod_steam_core_data")]
    [Serializable]
    public class mod_steam_core_data : RobotoModuleDataTemplate
    {
        public string steamAPIKey = "YOURAPIKEYHERE";
        public string steamCoreURL = "http://api.steampowered.com";
        public List<mod_steam_game> games = new List<mod_steam_game>();
        internal mod_steam_core_data() { }

        public bool tryAddGame(mod_steam_game g)
        {
            if (games.Where(x => x.gameID == g.gameID ).Count() == 0 )
            {
                games.Add(g);
                return true;
            }
            else
            {
                return false;
            }
        }

        public mod_steam_game getGame(string gameID)
        {
            List< mod_steam_game> matches = games.Where(x => x.gameID == gameID).ToList<mod_steam_game>();

            if (matches.Count == 0) { Console.WriteLine("Couldnt find game " + gameID); }
            //todo - cleanup should look for these
            else if (matches.Count > 1) { Console.WriteLine("Found multiple matches for game " + gameID); }
            else
            {
                return matches[0];
            }
            return null;
        }
    }


    /// <summary>
    /// ChatData to be stored in the XML store
    /// </summary>
    [XmlType("mod_steam_chat_data")]
    [Serializable]
    public class mod_steam_chat_data : RobotoModuleChatDataTemplate
    {
        public List<mod_steam_player> players = new List<mod_steam_player>();

        public void addPlayer(mod_steam_player player)
        {
            

            players.Add(player);
        }
        //internal mod_quote_data() { }
    }

    /// <summary>
    /// Represents a player (steamID)
    /// </summary>
    [XmlType("mod_steam_player")]
    [Serializable]
    public class mod_steam_player
    {

        public DateTime lastChecked = DateTime.Now;
        public string currentlyPlaying = "";
        public string playerName = "";
        public string playerID = "";
        public int chatID = -1;
        public List<mod_steam_chiev> chievs = new List<mod_steam_chiev>();

        internal mod_steam_player() { }
        public mod_steam_player(int chatID, String playerID, string playerName)
        {
            this.chatID = chatID;
            this.playerID = playerID;
            this.playerName = playerName;
        }

        public void checkAchievements()
        {
            //get a list of what the player has been playing
            List<mod_steam_game> playerGames = mod_steam_steamapi.getRecentGames(playerID);
            List<string> announce = new List<string>();

            foreach (mod_steam_game g in playerGames)
            {
                //get the achievement list for each game
                List<string> gainedAchievements = mod_steam_steamapi.getAchievements(playerID, g.gameID);

                //make a list of any that we havent recorded yet
                List<string> newAchievements = new List<string>();
                foreach (string achievementCode in gainedAchievements)
                {
                    if (chievs.Where(x => x.chievName == achievementCode && x.appID == g.gameID).Count() == 0 )
                    {
                        newAchievements.Add(achievementCode);
                    }
                }

                if (newAchievements.Count() > 0)
                {
                    //refresh the master list


                    //add them to our player's stash
                    foreach (string s in newAchievements)
                    {
                        chievs.Add(new mod_steam_chiev(s, g.gameID));
                        //todo - get friendly name
                        announce.Add(s + " in " + g.displayName);
                    }
                }
            }
            //send a message (first 3 per game)
            if (announce.Count() > 0)
            {
                string message = playerName + " got the following achievements:" + "\n\r";
                int max = 5;
                if (announce.Count < 5) { max = announce.Count; }
                    
                for (int i = 0; i < max; i++)
                {
                    message += "- " + announce[i] + "\n\r"; 
                }
                if (announce.Count > 5)
                {
                    message += "And (" + (announce.Count - 5).ToString() + ") others";
                }
                TelegramAPI.SendMessage(this.chatID, message);
            }

        }
    }

    /// <summary>
    /// Represents an instance of a player gaining a chiev
    /// </summary>
    [XmlType("mod_steam_chiev")]
    [Serializable]
    public class mod_steam_chiev
    {

        public DateTime addedOn = DateTime.Now;
        public string chievName = "";
        public string appID = "";
        
        internal mod_steam_chiev() { }
        public mod_steam_chiev(String chievName, string appID)
        {
            this.chievName = chievName;
            this.appID = appID;
        }
    }


    /// <summary>
    /// Represents an achievement - i.e. the achievement itself, not the player achieving it. 
    /// </summary>
    [XmlType("mod_steam_game")]
    [Serializable]
    public class mod_steam_game
    {
        public String gameID = "";
        public String displayName = "";
        public List<mod_steam_achievement> chievs = new List<mod_steam_achievement>();

        internal mod_steam_game() { }
        public mod_steam_game(String gameID, String displayName)
        {
            this.gameID = gameID;
            this.displayName = displayName;
        }
    }


    /// <summary>
    /// Represents a single achievement
    /// </summary>
    [XmlType("mod_steam_achievement")]
    [Serializable]
    public class mod_steam_achievement
    {
        public String achievement_code = "";
        public String displayName = "";
        public String description = "";

        public DateTime on = DateTime.Now;

        internal mod_steam_achievement() { }
        public mod_steam_achievement(String achievement_code, String displayName, String description)
        {
            this.achievement_code = achievement_code;
            this.displayName = displayName;
            this.description = description;
        }
    }

    public class mod_steam : RobotoModuleTemplate
    {
        private mod_steam_core_data localData;

        public override void init()
        {
            pluginChatDataType = typeof(mod_steam_chat_data);
            pluginDataType = typeof(mod_steam_core_data);

            chatHook = true;
            chatEvenIfAlreadyMatched = false;
            chatPriority = 5;
            this.backgroundHook = true;
            this.backgroundMins = 10;
            
        }

        public override string getMethodDescriptions()
        {
            return
                "steam_addplayer - Adds a player by steamID" + "\n\r" +
                "steam_removeplayer - Removes a player" + "\n\r" +
                "steam_help - outputs help";
        }

        public override void initData()
        {
            try
            {
                localData = Roboto.Settings.getPluginData<mod_steam_core_data>();
            }
            catch (InvalidDataException)
            {
                //Data doesnt exist, create, populate with sample data and register for saving
                localData = new mod_steam_core_data();
                sampleData();
                Roboto.Settings.registerData(localData);
            }
        }

        public override void initChatData(chat c)
        {
            mod_steam_chat_data chatData = c.getPluginData<mod_steam_chat_data>();

            if (chatData == null)
            {
                //Data doesnt exist, create, populate with sample data and register for saving
                chatData = new mod_steam_chat_data();
                c.addChatData(chatData);
            }

        }
        

        public override bool chatEvent(message m, chat c = null)
        {
            bool processed = false;
            if (c != null)
            {
                if (m.text_msg.StartsWith("/steam_addplayer"))
                {
                    TelegramAPI.GetExpectedReply(c.chatID, m.userID
                        , "Enter the steamID of the player you want to add. /steam_help to find out how to get this."
                        , false
                        , typeof(mod_steam)
                        , "ADDPLAYER", m.message_id, true);
                    processed = true;
                }
                else if (m.text_msg.StartsWith("/steam_help"))
                {
                    TelegramAPI.SendMessage(m.chatID, "You are looking for an ID from the Steam Community site, try http://steamcommunity.com/ and find your profile. You should have something like http://steamcommunity.com/profiles/01234567890132456 . Take this number on the end of the URL." 
                        ,false, m.message_id);
                    processed = true;
                }
                else if (m.text_msg.StartsWith("/steam_check"))
                {
                    checkChat(c);
                    

                    processed = true;
                }

            }
            return processed;
        }

        /// <summary>
        /// checks the players in the chat, if neccessary, for new achievs.
        /// </summary>
        /// <param name="c"></param>
        private void checkChat(chat c)
        {

            mod_steam_chat_data localData = c.getPluginData<mod_steam_chat_data>();
            foreach (mod_steam_player player in localData.players)
            {
                player.checkAchievements();
            }

        }

        protected override void backgroundProcessing()
        {
            foreach (chat c in Roboto.Settings.chatData)
            {
                checkChat(c);
                
                


            }
        }

        public override void sampleData()
        {
          
        }

        public override bool replyReceived(ExpectedReply e, message m)
        { 
            bool processed = false;
            chat c = Roboto.Settings.getChat(e.chatID);
            mod_steam_chat_data chatData = c.getPluginData<mod_steam_chat_data>();

            //Adding a player to the chat. We should have a player ID in our message. 
            if (e.messageData == "ADDPLAYER")
            {
                
                long playerID = -1;
                bool success = false;
                if (long.TryParse(m.text_msg, out playerID) && playerID >= -1)
                {
                    //get the steam profile
                    mod_steam_player player = mod_steam_steamapi.getPlayerInfo(playerID, c.chatID);
                    chatData.addPlayer(player);
                    TelegramAPI.SendMessage(c.chatID, "Added " + player.playerName + ". Any steam achievements will be announced.",false,m.message_id);

                    success = true;




                }
                
                if (!success)
                {
                    TelegramAPI.GetExpectedReply(m.chatID, m.userID, m.text_msg + " is not a valid playerID. Enter a valid playerID", false, typeof(mod_steam), "ADDPLAYER", m.message_id, true);
                }
                processed = true;
            }

            return processed;
        }
    }
}
