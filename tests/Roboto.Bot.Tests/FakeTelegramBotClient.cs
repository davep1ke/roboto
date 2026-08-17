using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Roboto.Bot.Tests;

public sealed record SentButton(string Text, string CallbackData);

public sealed record SentMessage(long ChatId, string Text, IReadOnlyList<SentButton>? Buttons = null);

public sealed record AnsweredCallback(string CallbackQueryId, string? Text);

/// <summary>
/// ITelegramBotClient is centered on a single method - SendRequest&lt;TResponse&gt; - with every
/// higher-level call (SendMessage, GetMe, ...) being an extension method that builds a typed
/// IRequest and funnels through it. Faking that one method (pattern-matching on request type)
/// covers everything built on top of it, discovered via reflection against the real interface
/// rather than guessed - see the introspection notes in this project's test-writing history if the
/// Telegram.Bot package version ever changes and this needs revisiting.
///
/// Only supports what the app actually calls today (GetMe, SendMessage, AnswerCallbackQuery). Add a
/// case + a real property read as soon as a command needs something else - don't pre-build support
/// speculatively. SendMessage also captures any InlineKeyboardMarkup's buttons (text + callback
/// data) onto SentMessage, so tests can find a button a fake "message" sent and tap it - see
/// TestBot.SendCallbackAsync.
/// </summary>
public sealed class FakeTelegramBotClient : ITelegramBotClient
{
    public List<SentMessage> SentMessages { get; } = [];
    public List<AnsweredCallback> AnsweredCallbacks { get; } = [];

    /// <summary>Chat ids that should fail to receive a DM, simulating a user who has never opened
    /// a private chat with the bot (real Telegram behaviour when you try to message such a user).</summary>
    public HashSet<long> UnreachableChatIds { get; } = [];

    public bool LocalBotServer => false;
    public long BotId => 999;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public IExceptionParser ExceptionsParser { get; set; } = null!;

    public event AsyncEventHandler<ApiRequestEventArgs>? OnMakingApiRequest { add { } remove { } }
    public event AsyncEventHandler<ApiResponseEventArgs>? OnApiResponseReceived { add { } remove { } }

    public Task<TResponse> SendRequest<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken)
    {
        switch (request)
        {
            case GetMeRequest:
                var me = new User { Id = BotId, IsBot = true, FirstName = "Fake", Username = "FakeBot" };
                return Task.FromResult((TResponse)(object)me);

            case SendMessageRequest sendMessage:
                var chatId = sendMessage.ChatId.Identifier
                    ?? throw new InvalidOperationException("FakeTelegramBotClient only supports numeric chat ids");

                if (UnreachableChatIds.Contains(chatId))
                {
                    throw new ApiRequestException("Forbidden: bot can't initiate conversation with a user", 403);
                }

                var buttons = sendMessage.ReplyMarkup is InlineKeyboardMarkup markup
                    ? markup.InlineKeyboard.SelectMany(row => row)
                        .Select(b => new SentButton(b.Text, b.CallbackData ?? ""))
                        .ToList()
                    : null;
                SentMessages.Add(new SentMessage(chatId, sendMessage.Text ?? "", buttons));
                var message = new Message
                {
                    Id = SentMessages.Count,
                    Chat = new Chat { Id = chatId },
                    Text = sendMessage.Text,
                };
                return Task.FromResult((TResponse)(object)message);

            case AnswerCallbackQueryRequest answerCallbackQuery:
                AnsweredCallbacks.Add(new AnsweredCallback(answerCallbackQuery.CallbackQueryId, answerCallbackQuery.Text));
                return Task.FromResult((TResponse)(object)true);

            default:
                throw new NotSupportedException(
                    $"FakeTelegramBotClient doesn't support {request.GetType().Name} - add a case for it.");
        }
    }

    public Task<bool> TestApi(CancellationToken cancellationToken) => Task.FromResult(true);

    public Task DownloadFile(string filePath, Stream destination, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not needed by any test yet.");

    public Task DownloadFile(TGFile file, Stream destination, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not needed by any test yet.");
}
