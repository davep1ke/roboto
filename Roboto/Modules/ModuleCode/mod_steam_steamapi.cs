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

namespace RobotoChatBot.Modules
{
    /// <summary>
    /// Methods for interacting with the Steam API. 
    /// </summary>
    static class mod_steam_steamapi
    {
        private static mod_steam_core_data getLocalData()
        {
            mod_steam_core_data localData = (mod_steam_core_data)Roboto.Settings.getPluginData<mod_steam_core_data>();
            return localData;
        }


        public static mod_steam_player getPlayerInfo(long playerID, long chatID)
        {
            NameValueCollection pairs = new NameValueCollection();
            pairs["steamids"] = playerID.ToString();
            JObject response = sendPOST(@"/ISteamUser/GetPlayerSummaries/v0002/", pairs);
            

            if (response != null)
            {
                //mangle this into a player object
                string playerName = "";
                bool isPrivate = false;
                try
                {
                    //get the message details
                    playerName = response.SelectToken("response.players[0].personaname").Value<string>();
                    int communityvisibilitystate = response.SelectToken("response.players[0].communityvisibilitystate").Value<int>();
                    if (communityvisibilitystate == 1)
                    {
                        Console.WriteLine(playerName + " is set to private");
                        isPrivate = true;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error parsing message " + e.ToString());

                }
                
                mod_steam_player player = new mod_steam_player(chatID, playerID.ToString(), playerName, isPrivate);
                return player;
                
            }
            else return null;


        }

        public static List<mod_steam_game> getRecentGames(string playerID)
        {
            mod_steam_core_data localData = getLocalData();
            List<mod_steam_game> games = new List<mod_steam_game>();
            NameValueCollection pairs = new NameValueCollection();
            pairs["steamid"] = playerID.ToString();
            JObject response = sendPOST(@"/IPlayerService/GetRecentlyPlayedGames/v0001/", pairs);

            foreach (JToken token in response.SelectTokens("response.games[*]"))//jo.Children()) //) records[*].data.importedPath"
            {
                //mangle this into a player object
                string gameName = "";
                string gameID = "";
                try
                {
                    //get the message details
                    gameName = token.SelectToken("name").Value<string>();
                    gameID = token.SelectToken("appid").Value<string>();
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error parsing message " + e.ToString());
                }

                //try get the game from our game cache
                mod_steam_game game = localData.getGame(gameID);

                if (game == null)
                {
                    //otherwise add it
                    game = new mod_steam_game(gameID, gameName);
                }
                games.Add(game);
            }

            //make sure these are all in our local cache
            foreach (mod_steam_game g in games)
            {
                bool added = localData.tryAddGame(g);
                if (added)
                {
                    Console.WriteLine("Added game " + g.displayName + " to the list of games we know about");
                }
            }
            return games;

        }

        public static List<string> getAchievements(string playerID, string gameID)
        {
            List<string> result = new List<string>();

            NameValueCollection pairs = new NameValueCollection();
            pairs["steamid"] = playerID;
            pairs["appid"] = gameID;

            JObject response = sendPOST(@"/ISteamUserStats/GetUserStatsForGame/v0002/", pairs);

            if (response != null)
            {
                foreach (JToken token in response.SelectTokens("playerstats.achievements[*]"))
                {
                    //mangle this into an achieve object
                    string achieveName = "";
                    try
                    {
                        //get the message details
                        achieveName = token.SelectToken("name").Value<string>();
                        result.Add(achieveName);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Error parsing message " + e.ToString());
                    }


                }
            }
            return result;
        }

        /// <summary>
        /// Get the list of achievements for a game
        /// </summary>
        /// <param name="gameID"></param>
        /// <returns></returns>
        public static List<mod_steam_achievement> getGameAchievements(string gameID)
        {
            List<mod_steam_achievement> result = new List<mod_steam_achievement>();

            NameValueCollection pairs = new NameValueCollection();
            pairs["appid"] = gameID;

            JObject response = sendPOST(@"/ISteamUserStats/GetSchemaForGame/v2/", pairs);

            if (response != null)
            {
                foreach (JToken token in response.SelectTokens("game.availableGameStats.achievements[*]"))
                {
                    //mangle this into an achieve object
                    try
                    {
                        string achievedescription = "";
                        string achieveCode = "";
                        string achieveDisplay = "";

                        //get the message details
                        JToken descToken = token.SelectToken("description");
                        JToken codeToken = token.SelectToken("name");
                        JToken dispToken = token.SelectToken("displayName");
                        
                        if (descToken != null) { achievedescription = descToken.Value<string>(); }
                        if (codeToken != null) { achieveCode = codeToken.Value<string>(); }
                        if (dispToken != null) { achieveDisplay = dispToken.Value<string>(); }

                        result.Add(new mod_steam_achievement(achieveCode, achieveDisplay, achievedescription));
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Error parsing message " + e.ToString());
                    }
                }
            }
            return result;
        }





        //TODO - merge this with the telegramAPI class, probably.

        /// <summary>
        /// Sends a POST message, returns the reply object
        /// </summary>
        /// <returns></returns>
        private static JObject sendPOST(String methodURL, NameValueCollection pairs)
        {
            //get our URL and Key
            mod_steam_core_data localData = getLocalData();
            pairs["key"] = localData.steamAPIKey;
            string finalString = localData.steamCoreURL + methodURL;

            //sort out encodings and clients.
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
            request.Method = "GET";
            request.ContentType = "application/json";
            Roboto.log.log("Calling Steam API:\n\r" + request.RequestUri.ToString(), logging.loglevel.low);
            try
            {

                HttpWebResponse webResponse = (HttpWebResponse)request.GetResponse();

                if (webResponse != null)
                {
                    StreamReader responseSR = new StreamReader(webResponse.GetResponseStream(), enc);
                    string response = responseSR.ReadToEnd();

                    JObject jo = JObject.Parse(response);

                    //success
                    //TODO - for Steam this is a "success" object that should return "true". Not the first item.
                    string path = jo.First.Path;

                    //if (path != "ok" || result != "True")
                    //{
                    //    Console.WriteLine("Error received sending message!");
                    //throw new WebException("Failure code from web service");

                    //}
                    Roboto.log.log("Message Success", logging.loglevel.low);
                    return jo;

                }
            }
            catch (Exception e)
            {
                Roboto.log.log("Steam API Call failed " + e.ToString(), logging.loglevel.critical);
                throw new WebException("Error during method call", e);
            }

            return null;
        }


    }
}
