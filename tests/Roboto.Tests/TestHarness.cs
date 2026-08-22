using RobotoChatBot;
using RobotoChatBot.Persistence;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace RobotoTests;

/// <summary>
/// Roboto is entirely static-global state (Roboto.Settings/Roboto.Store/Plugins.plugins/TelegramAPI's
/// cached client) by design - this port deliberately kept legacy's structure rather than introducing
/// DI, so there's no way to give each test its own isolated instance the way a DI container would.
/// Reset() instead points every static at a fresh temp SQLite DB and a fresh in-memory settings
/// graph before each test - see AssemblyInfo.cs for why this means tests can never run in parallel
/// with each other.
///
/// Plugins.plugins itself (the module *objects*, not their data) is only ever populated once per
/// process - Plugins.initPluginAssemblies() has a real bug (see its own comment, and Plugins.
/// ResetPluginDataForTesting's) that makes re-scanning add duplicates, so this harness scans once and
/// only resets each plugin's cached data afterwards.
/// </summary>
public sealed class TestHarness : IDisposable
{
    private static bool _pluginsLoaded;

    public FakeTelegramBotClient BotClient { get; } = new();
    private readonly string _dbPath;

    public TestHarness()
    {
        if (!_pluginsLoaded)
        {
            Plugins.initPluginAssemblies();
            _pluginsLoaded = true;
        }
        else
        {
            Plugins.ResetPluginDataForTesting();
        }

        _dbPath = Path.Combine(Path.GetTempPath(), $"roboto-test-{Guid.NewGuid():N}.db");
        Roboto.Options = new BotOptions
        {
            Instance = "test",
            DataDir = Path.GetTempPath(),
            TelegramToken = "unused-in-tests",
            BotUsername = "TestBot",
        };
        Roboto.Store = new SqliteStateStore(_dbPath);
        Roboto.Store.Initialize();

        TelegramAPI.SetClientForTesting(BotClient);

        // Mirrors Roboto.cs's startBackground() ordering, minus log.load() (file logging - not
        // needed for tests, console+DB logging already works via logging's own static-field-
        // initializer-time constructor) and the final Settings.save() (tests operate on in-memory
        // state directly; call Roboto.Settings.save() explicitly if a test specifically wants to
        // verify a persistence round-trip).
        Roboto.Settings = settings.load();
        Roboto.Settings.stats.startup();
        Plugins.startupChecks();
    }

    /// <summary>Runs one full background-processing pass synchronously, bypassing
    /// BackgroundScheduler's real thread/60s timer entirely - force:true so each module's own
    /// per-type throttle (backgroundMins) can't skip it, making this deterministic for tests instead
    /// of time-dependent.</summary>
    public void RunBackgroundProcessing() => Plugins.backgroundProcessing(true);

    public void Send(Message message) => TelegramAPI.DispatchUpdate(new Update { Message = message });

    /// <summary>Simulates someone promoting the bot to admin in a group - fires the same
    /// MyChatMember update TelegramAPI.DispatchUpdate's bot self-de-admin handling (phase 9) reacts
    /// to. BotClient.BotId is the bot's own user id, matching what TelegramAPI.Client.GetMe would
    /// return for the real client.</summary>
    public void PromoteBotToAdmin(long chatId, string title = "Test Group") => TelegramAPI.DispatchUpdate(new Update
    {
        MyChatMember = new ChatMemberUpdated
        {
            Chat = new Chat { Id = chatId, Type = ChatType.Group, Title = title },
            From = new User { Id = 1, FirstName = "Someone" },
            Date = DateTime.UtcNow,
            OldChatMember = new ChatMemberMember { User = new User { Id = BotClient.BotId, IsBot = true, FirstName = "TestBot" } },
            NewChatMember = new ChatMemberAdministrator { User = new User { Id = BotClient.BotId, IsBot = true, FirstName = "TestBot" } },
        },
    });

    public void SendGroupMessage(long chatId, long userId, string text, string firstName = "Test", string title = "Test Group", Message replyTo = null) =>
        Send(GroupMessage(chatId, userId, text, firstName, title, replyTo));

    public void SendPrivateMessage(long userId, string text, string firstName = "Test", Message replyTo = null) =>
        Send(PrivateMessage(userId, text, firstName, replyTo));

    /// <summary>Simulates tapping a ReplyKeyboardMarkup button - legacy's actual keyboard model
    /// (see ExpectedReply.keyboard's own comment): a tap just sends the button's exact label as a
    /// new private message. ExpectedReply's own match predicate (m.chatID == e.userID) is satisfied
    /// by any private message from that user, so this needs no reply-to-message-id wiring at all for
    /// the DM-based flows every settings/game menu actually uses.</summary>
    public void TapButton(long userId, string buttonText, string firstName = "Test") =>
        SendPrivateMessage(userId, buttonText, firstName);

    /// <summary>The most recent message sent to this chat/user carrying a keyboard - for finding a
    /// button to tap, or asserting on what a menu currently offers.</summary>
    public SentMessage LastKeyboardMessageTo(long chatId) =>
        BotClient.SentMessages.Last(m => m.ChatId == chatId && m.KeyboardRows is { Count: > 0 });

    public static Message GroupMessage(long chatId, long userId, string text, string firstName = "Test", string title = "Test Group", Message replyTo = null) => new()
    {
        Id = Random.Shared.Next(1, int.MaxValue),
        Chat = new Chat { Id = chatId, Type = ChatType.Group, Title = title },
        From = new User { Id = userId, FirstName = firstName },
        Text = text,
        ReplyToMessage = replyTo,
    };

    public static Message PrivateMessage(long userId, string text, string firstName = "Test", Message replyTo = null) => new()
    {
        Id = Random.Shared.Next(1, int.MaxValue),
        Chat = new Chat { Id = userId, Type = ChatType.Private },
        From = new User { Id = userId, FirstName = firstName },
        Text = text,
        ReplyToMessage = replyTo,
    };

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }
}
