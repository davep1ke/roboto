using System.Collections.Generic;
using System.IO;

namespace RobotoChatBot
{
    /// <summary>
    /// Each bot identity is a subfolder under DataDir: {DataDir}/{Instance}/bot.env holds its
    /// credentials, {DataDir}/{Instance}/roboto.db holds its SQLite state. Mirrors legacy's own
    /// "first run creates a blank config, fill it in and restart" flow (settings.load()'s
    /// isFirstTimeInitialised path) - same idea, a per-instance folder instead of one XML file per
    /// -context, and credentials in their own small file instead of baked into the same document as
    /// every chat's live game state.
    /// </summary>
    public static class InstanceBootstrapper
    {
        private const string StubContent =
            """
            # Roboto bot config for this instance. Fill in TelegramToken (create a bot via @BotFather,
            # or reuse an existing test bot's token) and restart.
            TelegramToken=
            BotUsername=

            # Optional - mod_steam's commands/background achievement checks are disabled until this is
            # set (a Steam Web API key, from https://steamcommunity.com/dev/apikey). Leaving it blank is
            # fine; nothing else in the bot depends on it.
            SteamApiKey=
            """;

        public static bool TryLoad(string dataDir, string instance, out string telegramToken, out string botUsername, out string steamApiKey, out string message)
        {
            var instanceDir = Path.Combine(dataDir, instance);
            var configPath = Path.Combine(instanceDir, "bot.env");

            Directory.CreateDirectory(instanceDir);

            if (!File.Exists(configPath))
            {
                File.WriteAllText(configPath, StubContent);
                telegramToken = "";
                botUsername = "";
                steamApiKey = "";
                message = $"No config found for instance '{instance}'. Created a starter file at " +
                          $"{configPath} - fill in TelegramToken and restart.";
                return false;
            }

            var values = Parse(configPath);
            telegramToken = values.TryGetValue("TelegramToken", out var t) ? t : "";
            botUsername = values.TryGetValue("BotUsername", out var b) ? b : "";
            steamApiKey = values.TryGetValue("SteamApiKey", out var s) ? s : "";

            if (string.IsNullOrWhiteSpace(telegramToken))
            {
                message = $"{configPath} exists but TelegramToken is still blank. Fill it in and restart.";
                return false;
            }

            message = "";
            return true;
        }

        private static Dictionary<string, string> Parse(string path)
        {
            var values = new Dictionary<string, string>();

            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                {
                    continue;
                }

                var separatorIndex = trimmed.IndexOf('=');
                if (separatorIndex < 0)
                {
                    continue;
                }

                var key = trimmed[..separatorIndex].Trim();
                var value = trimmed[(separatorIndex + 1)..].Trim();
                values[key] = value;
            }

            return values;
        }
    }
}
