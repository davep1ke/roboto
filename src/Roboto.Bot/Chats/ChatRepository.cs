using Roboto.Bot.Persistence;

namespace Roboto.Bot.Chats;

public sealed class ChatRepository(IStateStore store)
{
    public async Task<ChatState> GetAsync(long chatId, CancellationToken cancellationToken)
    {
        return await store.LoadAsync<ChatState>(Key(chatId), cancellationToken)
               ?? new ChatState { ChatId = chatId };
    }

    public Task SaveAsync(ChatState chat, CancellationToken cancellationToken) =>
        store.SaveAsync(Key(chat.ChatId), chat, cancellationToken);

    /// <summary>Bumps LastActiveUtc for a chat - called from MessageDispatcher on every incoming
    /// message/callback, mirrors legacy's chat.resetLastUpdateTime(). Drives ChatPurgeReconciler.</summary>
    public async Task TouchAsync(long chatId, CancellationToken cancellationToken)
    {
        var chat = await GetAsync(chatId, cancellationToken);
        chat.LastActiveUtc = DateTime.UtcNow;
        await SaveAsync(chat, cancellationToken);
    }

    /// <summary>Every known chat - for ChatPurgeReconciler's dormant-chat sweep. Filters to keys
    /// shaped exactly "chat:{chatId}": a plain LIKE 'chat:%' would also match longer keys sharing
    /// this prefix (e.g. quiet-hours' "chat:{chatId}:quiet-hours"), which would then fail to
    /// deserialize as a ChatState cleanly - SQL LIKE alone can't express "no further colons".</summary>
    public async Task<IReadOnlyList<ChatState>> GetAllAsync(CancellationToken cancellationToken)
    {
        var chats = new List<ChatState>();
        foreach (var key in await store.LoadAllKeysAsync("chat:%", cancellationToken))
        {
            if (!long.TryParse(key.AsSpan("chat:".Length), out _))
            {
                continue;
            }

            if (await store.LoadAsync<ChatState>(key, cancellationToken) is { } chat)
            {
                chats.Add(chat);
            }
        }

        return chats;
    }

    public Task DeleteAsync(long chatId, CancellationToken cancellationToken) =>
        store.DeleteAsync(Key(chatId), cancellationToken);

    private static string Key(long chatId) => $"chat:{chatId}";
}
