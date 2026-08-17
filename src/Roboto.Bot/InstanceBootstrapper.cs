namespace Roboto.Bot;

/// <summary>
/// Each bot identity is a subfolder under DataDir: {DataDir}/{Instance}/bot.env holds its
/// credentials, {DataDir}/{Instance}/roboto.db will hold its SQLite state (once that lands).
/// Mirrors the legacy app's "first run creates a blank config, fill it in and restart" flow -
/// same idea, per-instance folder instead of one XML file per -context.
/// </summary>
public static class InstanceBootstrapper
{
    private const string StubContent =
        """
        # Roboto bot config for this instance. Fill in TelegramToken (create a bot via @BotFather,
        # or reuse an existing test bot's token) and restart.
        TelegramToken=
        BotUsername=
        """;

    public static bool TryLoad(string dataDir, string instance, out string telegramToken, out string botUsername, out string message)
    {
        var instanceDir = Path.Combine(dataDir, instance);
        var configPath = Path.Combine(instanceDir, "bot.env");

        Directory.CreateDirectory(instanceDir);

        if (!File.Exists(configPath))
        {
            File.WriteAllText(configPath, StubContent);
            telegramToken = "";
            botUsername = "";
            message = $"No config found for instance '{instance}'. Created a starter file at " +
                      $"{configPath} - fill in TelegramToken and restart.";
            return false;
        }

        var values = Parse(configPath);
        telegramToken = values.GetValueOrDefault("TelegramToken", "");
        botUsername = values.GetValueOrDefault("BotUsername", "");

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
