using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace RobotoTests;

public sealed record SentButton(string Text, int MessageId);

public sealed record SentMessage(long ChatId, string Text, IReadOnlyList<IReadOnlyList<SentButton>>? KeyboardRows = null, int Id = 0);

public sealed record SentPhoto(long ChatId, string? Caption, byte[] Content);

/// <summary>
/// ITelegramBotClient is centered on a single method - SendRequest&lt;TResponse&gt; - with every
/// higher-level call (SendMessage, GetMe, ...) being an extension method that builds a typed
/// IRequest and funnels through it (same discovery as the abandoned rewrite branch's own fake, which
/// this is adapted from). Faking that one method covers everything built on top of it.
///
/// Only supports what TelegramAPI.cs actually calls today (GetMe, SendMessage, SendPhoto,
/// GetChatMemberCount) - add a case as soon as something needs it, don't pre-build speculatively.
/// Keeps the full keyboard row structure (unlike the old rewrite's fake, which flattened rows - see
/// KeyboardColumnLayoutTests on that branch for why that mattered there) since this codebase's
/// button-tap "taps" are actually simulated as typed replies (ReplyKeyboardMarkup, not
/// InlineKeyboardMarkup - see TestHarness.TapButton), so a test needs the real per-row label list to
/// find the right button, and preserving row shape costs nothing extra.
/// </summary>
public sealed class FakeTelegramBotClient : ITelegramBotClient
{
    public List<SentMessage> SentMessages { get; } = [];
    public List<SentPhoto> SentPhotos { get; } = [];

    /// <summary>Chat ids that should fail to receive a DM, simulating a user who has never opened
    /// a private chat with the bot (real Telegram behaviour when you try to message such a user).</summary>
    public HashSet<long> UnreachableChatIds { get; } = [];

    public bool LocalBotServer => false;
    public long BotId => 999;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public IExceptionParser ExceptionsParser { get; set; } = null!;

    public event AsyncEventHandler<Telegram.Bot.Args.ApiRequestEventArgs>? OnMakingApiRequest { add { } remove { } }
    public event AsyncEventHandler<Telegram.Bot.Args.ApiResponseEventArgs>? OnApiResponseReceived { add { } remove { } }

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

                var sentId = SentMessages.Count + SentPhotos.Count + 1;
                var rows = sendMessage.ReplyMarkup switch
                {
                    ReplyKeyboardMarkup rk => rk.Keyboard.Select(row => (IReadOnlyList<SentButton>)row.Select(b => new SentButton(b.Text, sentId)).ToList()).ToList(),
                    _ => null,
                };
                SentMessages.Add(new SentMessage(chatId, sendMessage.Text ?? "", rows, sentId));
                var message = new Message
                {
                    Id = sentId,
                    Chat = new Chat { Id = chatId },
                    Text = sendMessage.Text,
                };
                return Task.FromResult((TResponse)(object)message);

            case SendPhotoRequest sendPhoto:
                var photoChatId = sendPhoto.ChatId.Identifier
                    ?? throw new InvalidOperationException("FakeTelegramBotClient only supports numeric chat ids");

                if (sendPhoto.Photo is InputFileStream fileStream)
                {
                    using var buffer = new MemoryStream();
                    fileStream.Content.CopyTo(buffer);
                    SentPhotos.Add(new SentPhoto(photoChatId, sendPhoto.Caption, buffer.ToArray()));
                }

                var photoMessage = new Message { Id = SentMessages.Count + SentPhotos.Count, Chat = new Chat { Id = photoChatId } };
                return Task.FromResult((TResponse)(object)photoMessage);

            case GetChatMemberCountRequest:
                return Task.FromResult((TResponse)(object)1);

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
