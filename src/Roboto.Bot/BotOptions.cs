namespace Roboto.Bot;

public sealed class BotOptions
{
    /// <summary>
    /// Which bot identity this process is running as. Its config and data both live under
    /// {DataDir}/{Instance}/ - self-created on first run if missing.
    /// </summary>
    public string Instance { get; set; } = "default";

    public string DataDir { get; set; } = "/data";

    // Populated from {DataDir}/{Instance}/bot.env by InstanceBootstrapper, not from ROBOTO_* env vars.
    public string TelegramToken { get; set; } = "";
    public string BotUsername { get; set; } = "";

    /// <summary>Optional - unlike TelegramToken, a blank value doesn't block startup. mod_steam's
    /// commands/background job just degrade to "not configured" rather than treating it as fatal,
    /// since most instances won't set it.</summary>
    public string SteamApiKey { get; set; } = "";

    /// <summary>
    /// {DataDir}/{Instance} - holds bot.env and roboto.db. InstanceBootstrapper computes this same
    /// path itself rather than reading it from here, since it runs before BotOptions is populated.
    /// </summary>
    public string InstanceDir => Path.Combine(DataDir, Instance);
}
