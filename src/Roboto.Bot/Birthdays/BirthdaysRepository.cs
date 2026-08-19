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

    /// <summary>For ChatPurgeReconciler - legacy's mod_birthdays.isPurgable() unconditionally
    /// refuses to purge once the module has ever been touched for a chat, regardless of whether any
    /// birthdays are still on the list (a deliberate legacy quirk, reproduced here rather than
    /// "fixed": once used, permanently protected). GetAsync's own "?? new" default can't
    /// distinguish "never touched" from "touched but now empty" - this checks the raw stored value
    /// directly instead.</summary>
    public async Task<bool> ExistsAsync(long chatId, CancellationToken cancellationToken) =>
        await store.LoadAsync<BirthdayChatState>(Key(chatId), cancellationToken) is not null;

    public Task DeleteAsync(long chatId, CancellationToken cancellationToken) =>
        store.DeleteAsync(Key(chatId), cancellationToken);

    private static string Key(long chatId) => $"birthdays:{chatId}";
}
