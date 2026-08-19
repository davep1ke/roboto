using Roboto.Bot.Persistence;

namespace Roboto.Bot.Quotes;

public sealed class QuotesRepository(IStateStore store)
{
    public async Task<QuoteChatState> GetAsync(long chatId, CancellationToken cancellationToken)
    {
        return await store.LoadAsync<QuoteChatState>(Key(chatId), cancellationToken)
               ?? new QuoteChatState { ChatId = chatId };
    }

    public Task SaveAsync(QuoteChatState chat, CancellationToken cancellationToken) =>
        store.SaveAsync(Key(chat.ChatId), chat, cancellationToken);

    /// <summary>Every chat with quote data at all - for QuotesReconciler to sweep on each
    /// scheduler tick, same reasoning as BirthdaysRepository.GetAllAsync.</summary>
    public Task<IReadOnlyList<QuoteChatState>> GetAllAsync(CancellationToken cancellationToken) =>
        store.LoadAllAsync<QuoteChatState>("quotes:%", cancellationToken);

    /// <summary>For ChatPurgeReconciler - legacy's mod_quote never blocks purge on existence alone,
    /// only on actually having quotes (see GetAsync().Quotes.Count), so deleting is always safe once
    /// that's already been checked.</summary>
    public Task DeleteAsync(long chatId, CancellationToken cancellationToken) =>
        store.DeleteAsync(Key(chatId), cancellationToken);

    private static string Key(long chatId) => $"quotes:{chatId}";
}
