using Roboto.Bot.Persistence;

namespace Roboto.Bot.Steam;

/// <summary>Covers both of mod_steam's storage shapes: one global achievement-schema cache
/// (SteamCoreState) and one per-chat tracked-player list (SteamChatState) - kept in a single
/// repository since both belong to the same module, same as every other module here owning its own
/// key scheme.</summary>
public sealed class SteamRepository(IStateStore store)
{
    private const string CoreKey = "steam:core";

    public async Task<SteamCoreState> GetCoreAsync(CancellationToken cancellationToken) =>
        await store.LoadAsync<SteamCoreState>(CoreKey, cancellationToken) ?? new SteamCoreState();

    public Task SaveCoreAsync(SteamCoreState core, CancellationToken cancellationToken) =>
        store.SaveAsync(CoreKey, core, cancellationToken);

    public async Task<SteamChatState> GetChatAsync(long chatId, CancellationToken cancellationToken)
    {
        return await store.LoadAsync<SteamChatState>(ChatKey(chatId), cancellationToken)
               ?? new SteamChatState { ChatId = chatId };
    }

    public Task SaveChatAsync(SteamChatState chat, CancellationToken cancellationToken) =>
        store.SaveAsync(ChatKey(chat.ChatId), chat, cancellationToken);

    /// <summary>Every chat tracking at least one player - for SteamReconciler to sweep on each
    /// scheduler tick, same reasoning as BirthdaysRepository/QuotesRepository.GetAllAsync.</summary>
    public Task<IReadOnlyList<SteamChatState>> GetAllChatsAsync(CancellationToken cancellationToken) =>
        store.LoadAllAsync<SteamChatState>("steam:chat:%", cancellationToken);

    /// <summary>For ChatPurgeReconciler - legacy's mod_steam never overrides isPurgable(), so
    /// tracked-player data is always purgable regardless of content once the chat itself is
    /// eligible; no existence/content check needed before deleting, unlike quotes/birthdays.</summary>
    public Task DeleteChatAsync(long chatId, CancellationToken cancellationToken) =>
        store.DeleteAsync(ChatKey(chatId), cancellationToken);

    private static string ChatKey(long chatId) => $"steam:chat:{chatId}";
}
