namespace Roboto.Bot;

/// <summary>
/// Each bot identity is a subfolder under DataDir: {DataDir}/{Instance}/bot.env holds its
/// credentials, {DataDir}/{Instance}/roboto.db holds its SQLite state. Mirrors the legacy app's
/// "first run creates a blank config, fill it in and restart" flow - same idea, per-instance
/// folder instead of one XML file per -context.
///
/// Superseded design, don't resurrect: credentials purely via ROBOTO_TELEGRAMTOKEN/
/// ROBOTO_BOTUSERNAME env vars, with a *.env file per test bot at the repo root picked via a
/// docker-compose ENV_FILE trick. Problems: spinning up a new instance meant hand-authoring a new
/// env file yourself, and the compose file only bind-mounted one fixed host path - nothing stopped
/// two concurrently-running instances from colliding and overwriting each other's data. The
/// current design fixes both: ROBOTO_INSTANCE is the only thing that varies per identity, and
/// every instance's data is a subfolder of one shared mount instead of needing its own host path.
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
        telegramToken = values.GetValueOrDefault("TelegramToken", "");
        botUsername = values.GetValueOrDefault("BotUsername", "");
        steamApiKey = values.GetValueOrDefault("SteamApiKey", "");

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
