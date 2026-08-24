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
/// GetChatMemberCount, PromoteChatMember, GetChatMember) - add a case as soon as something needs
/// it, don't pre-build speculatively.
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

    /// <summary>Records every *successful* PromoteChatMember call - TelegramAPI's bot self-de-admin
    /// (phase 9) calls this with every permission left at its default false, the only "demote"
    /// mechanism the real API has.</summary>
    public List<(long ChatId, long UserId)> PromoteChatMemberCalls { get; } = [];

    /// <summary>Counts every PromoteChatMemberRequest *attempt*, including ones that go on to throw
    /// (AdminActionBlockedChatIds/BasicGroupChatIds) - unlike PromoteChatMemberCalls above, which
    /// only records successes. For DeAdminSelf's retry-throttle tests, where what matters is whether
    /// the API call was attempted at all, not whether it succeeded.</summary>
    public int PromoteChatMemberAttempts { get; private set; }

    /// <summary>Chat ids that should fail to receive a DM, simulating a user who has never opened
    /// a private chat with the bot (real Telegram behaviour when you try to message such a user).</summary>
    public HashSet<long> UnreachableChatIds { get; } = [];

    /// <summary>Chat ids that are basic (non-super) groups - PromoteChatMemberRequest genuinely
    /// fails for these on real Telegram (the live "400 Bad Request: method is available for
    /// supergroup and channel chats only" this project hit), even though a human can still make
    /// the bot admin there through the Telegram app's own UI.</summary>
    public HashSet<long> BasicGroupChatIds { get; } = [];

    /// <summary>Chat ids where GetChatMemberRequest should report the bot itself as currently an
    /// administrator - for EnsureNotAdminInAnyChat's background-sweep tests. PromoteChatMemberRequest
    /// removes a chat from this set (successfully de-admining, same as real Telegram).</summary>
    public HashSet<long> ChatsWhereBotIsAdmin { get; } = [];

    /// <summary>Chat ids where GetChatMemberRequest should fail as if the chat itself no longer
    /// exists (bot removed / chat deleted) - the real "chat not found" error found live - for
    /// EnsureNotAdminInAnyChat's confirmed-gone detection tests.</summary>
    public HashSet<long> GoneChatIds { get; } = [];

    /// <summary>Chat ids where PromoteChatMemberRequest should fail with CHAT_ADMIN_REQUIRED - the
    /// real "bot has some admin flag but can't act on it" permanent state found live - for
    /// DeAdminSelf's retry-throttle tests.</summary>
    public HashSet<long> AdminActionBlockedChatIds { get; } = [];

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

            case PromoteChatMemberRequest promote:
                var promoteChatId = promote.ChatId.Identifier
                    ?? throw new InvalidOperationException("FakeTelegramBotClient only supports numeric chat ids");
                PromoteChatMemberAttempts++;
                if (AdminActionBlockedChatIds.Contains(promoteChatId))
                {
                    throw new ApiRequestException("Bad Request: CHAT_ADMIN_REQUIRED", 400);
                }
                if (BasicGroupChatIds.Contains(promoteChatId))
                {
                    throw new ApiRequestException("Bad Request: method is available for supergroup and channel chats only", 400);
                }
                PromoteChatMemberCalls.Add((promoteChatId, promote.UserId));
                ChatsWhereBotIsAdmin.Remove(promoteChatId);
                return Task.FromResult((TResponse)(object)true);

            case GetChatMemberRequest getMember:
                var getMemberChatId = getMember.ChatId.Identifier
                    ?? throw new InvalidOperationException("FakeTelegramBotClient only supports numeric chat ids");
                if (GoneChatIds.Contains(getMemberChatId))
                {
                    throw new ApiRequestException("Bad Request: chat not found", 400);
                }
                ChatMember member = ChatsWhereBotIsAdmin.Contains(getMemberChatId)
                    ? new ChatMemberAdministrator { User = new User { Id = getMember.UserId, IsBot = true } }
                    : new ChatMemberMember { User = new User { Id = getMember.UserId, IsBot = true } };
                return Task.FromResult((TResponse)(object)member);

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
