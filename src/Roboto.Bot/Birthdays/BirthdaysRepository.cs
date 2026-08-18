using Roboto.Bot.Persistence;

namespace Roboto.Bot.Birthdays;

public sealed class BirthdaysRepository(IStateStore store)
{
    public async Task<BirthdayChatState> GetAsync(long chatId, CancellationToken cancellationToken)
    {
        return await store.LoadAsync<BirthdayChatState>(Key(chatId), cancellationToken)
               ?? new BirthdayChatState { ChatId = chatId };
    }

    public Task SaveAsync(BirthdayChatState chat, CancellationToken cancellationToken) =>
        store.SaveAsync(Key(chat.ChatId), chat, cancellationToken);

    /// <summary>Every chat with birthday data at all - for BirthdaysReconciler to sweep on each
    /// scheduler tick. Unlike XyzzyGameRepository's active-games query, there's no "stopped" status
    /// to filter on here; every chat that's ever added a birthday stays relevant forever.</summary>
    public Task<IReadOnlyList<BirthdayChatState>> GetAllAsync(CancellationToken cancellationToken) =>
        store.LoadAllAsync<BirthdayChatState>("birthdays:%", cancellationToken);

    private static string Key(long chatId) => $"birthdays:{chatId}";
}
