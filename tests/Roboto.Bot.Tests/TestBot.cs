using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Roboto.Bot.Commands;
using Roboto.Bot.Persistence;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Tests;

/// <summary>
/// Builds the exact same service graph as Program.cs (via AddRobotoBot) but with a
/// FakeTelegramBotClient instead of a real one and a temp-directory-backed SQLite file instead of
/// /data - a fresh, isolated instance per test (xUnit constructs a new test class per test method
/// by default), cleaned up on Dispose.
///
/// Bypasses InstanceBootstrapper entirely - tests configure BotOptions directly rather than going
/// through the "read bot.env, prompt if missing" file-based bootstrap, which has nothing to do with
/// application logic and isn't what these tests are for.
/// </summary>
public sealed class TestBot : IDisposable
{
    private readonly string _dataDir;
    private readonly bool _ownsDataDir;

    public FakeTelegramBotClient BotClient { get; } = new();
    public ServiceProvider Services { get; }

    public TestBot() : this(Directory.CreateTempSubdirectory("roboto-tests-").FullName, ownsDataDir: true)
    {
    }

    private TestBot(string dataDir, bool ownsDataDir)
    {
        _dataDir = dataDir;
        _ownsDataDir = ownsDataDir;

        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<BotOptions>(o =>
        {
            o.Instance = "test";
            o.DataDir = _dataDir;
            o.TelegramToken = "unused-in-tests";
            o.BotUsername = "TestBot";
        });
        services.AddRobotoBot();

        Services = services.BuildServiceProvider();

        // InstanceBootstrapper normally creates this before the store ever touches disk - do the
        // same thing here since tests skip InstanceBootstrapper entirely.
        Directory.CreateDirectory(Services.GetRequiredService<IOptions<BotOptions>>().Value.InstanceDir);
        Services.GetRequiredService<IStateStore>().InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public MessageDispatcher Dispatcher => Services.GetRequiredService<MessageDispatcher>();

    /// <summary>Taps a player's current hand-keyboard card, then keeps tapping their next hand
    /// keyboard for as long as SubmitAnswerAsync reports "pick your next card" - transparently
    /// handles both single- and multi-answer questions, so existing single-answer call sites don't
    /// need to know or care which kind of question is actually in play. A single-answer question
    /// still resolves in exactly one tap, same as before multi-answer support existed.</summary>
    public async Task AnswerHandFullyAsync(long playerId)
    {
        while (true)
        {
            var handMessage = BotClient.SentMessages.Last(m => m.ChatId == playerId && m.Buttons is { Count: > 0 });
            await SendCallbackAsync(playerId, handMessage.Buttons![0]);
            if (BotClient.AnsweredCallbacks[^1].Text != "Answer submitted! Pick your next card.")
            {
                break;
            }
        }
    }

    public Task SendAsync(Message message, CancellationToken cancellationToken = default) =>
        Dispatcher.DispatchAsync(BotClient, new Update { Message = message }, cancellationToken);

    /// <summary>Simulates a user tapping an inline-keyboard button (a CallbackQuery update) - pass
    /// the actual SentButton (see FakeTelegramBotClient.SentMessages) so the tap carries the real
    /// message ID CallbackQueryRouter now checks against the user's DmOutbox head (phase 8.9), not
    /// just the callback data.</summary>
    public Task SendCallbackAsync(long userId, SentButton button, string firstName = "Test", CancellationToken cancellationToken = default) =>
        SendCallbackAsync(userId, button.CallbackData, button.MessageId, firstName, cancellationToken);

    /// <summary>Lower-level version for tests that need to simulate a tap on a specific (possibly
    /// forged-data or deliberately-wrong-message-id) button rather than a real SentButton - e.g.
    /// proving a stale or tampered tap is rejected.</summary>
    public Task SendCallbackAsync(long userId, string callbackData, int messageId, string firstName = "Test", CancellationToken cancellationToken = default)
    {
        var update = new Update
        {
            CallbackQuery = new CallbackQuery
            {
                Id = Guid.NewGuid().ToString(),
                From = new User { Id = userId, FirstName = firstName },
                Message = new Message { Id = messageId, Chat = new Chat { Id = userId, Type = ChatType.Private } },
                Data = callbackData,
            },
        };
        return Dispatcher.DispatchAsync(BotClient, update, cancellationToken);
    }

    /// <summary>
    /// Simulates a full process restart against the same on-disk data: a fresh service provider
    /// (fresh singletons, nothing carried over in memory) pointed at the same DataDir, so persisted
    /// state genuinely has to come from disk to be seen. The original TestBot still owns and will
    /// clean up the temp directory on Dispose; this one doesn't delete it.
    /// </summary>
    public TestBot Restart() => new(_dataDir, ownsDataDir: false);

    public void Dispose()
    {
        Services.Dispose();
        if (_ownsDataDir)
        {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    public static Message PrivateMessage(long userId, string text, string firstName = "Test", Message? replyTo = null) => new()
    {
        Id = Random.Shared.Next(1, int.MaxValue),
        Chat = new Chat { Id = userId, Type = ChatType.Private },
        From = new User { Id = userId, FirstName = firstName },
        Text = text,
        ReplyToMessage = replyTo,
    };

    /// <summary>Shorthand for replying to a specific SentMessage (one ReplyRouter is currently
    /// tracking) rather than building a stub Message by hand just to carry its ID.</summary>
    public static Message ReplyTo(SentMessage question) => new() { Id = question.Id };

    public static Message GroupMessage(long chatId, long userId, string text, string firstName = "Test", Message? replyTo = null, string title = "Test Group") => new()
    {
        Id = Random.Shared.Next(1, int.MaxValue),
        Chat = new Chat { Id = chatId, Type = ChatType.Group, Title = title },
        From = new User { Id = userId, FirstName = firstName },
        Text = text,
        ReplyToMessage = replyTo,
    };
}
