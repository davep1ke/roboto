using System.Collections.Generic;
using System.IO;

namespace RobotoChatBot
{
    /// <summary>
    /// Replaces the legacy -context CLI flag + %appdata%\Roboto\&lt;context&gt;.xml path scheme.
    /// Which bot identity this process is running as - its config (bot.env) and data (roboto.db)
    /// both live under {DataDir}/{Instance}/, self-created on first run if missing by
    /// InstanceBootstrapper. Held as a single instance on Roboto.Options, matching this codebase's
    /// existing static-globals convention (Roboto.Settings, Roboto.log).
    /// </summary>
    public sealed class BotOptions
    {
        public string Instance { get; set; } = "default";

        public string DataDir { get; set; } = "/data";

        // Populated from {DataDir}/{Instance}/bot.env by InstanceBootstrapper, not from env vars
        // directly.
        public string TelegramToken { get; set; } = "";
        public string BotUsername { get; set; } = "";

        /// <summary>Optional - unlike TelegramToken, a blank value doesn't block startup. mod_steam's
        /// commands/background job just degrade to "not configured" rather than treating it as
        /// fatal, since most instances won't set it.</summary>
        public string SteamApiKey { get; set; } = "";

        /// <summary>Optional module allow-list (bot.env's "Plugins" line) - empty means every module
        /// loads (the default). Merged with any -plugin CLI args into Roboto.pluginFilter at
        /// startup, not read directly by module code.</summary>
        public List<string> Plugins { get; set; } = new List<string>();

        /// <summary>
        /// {DataDir}/{Instance} - holds bot.env and roboto.db. InstanceBootstrapper computes this
        /// same path itself rather than reading it from here, since it runs before BotOptions is
        /// populated.
        /// </summary>
        public string InstanceDir => Path.Combine(DataDir, Instance);
    }
}
