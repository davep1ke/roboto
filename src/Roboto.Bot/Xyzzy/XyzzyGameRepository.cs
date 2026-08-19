using Roboto.Bot.Persistence;

namespace Roboto.Bot.Xyzzy;

public sealed class XyzzyGameRepository(IStateStore store)
{
    public async Task<XyzzyGameState> GetAsync(long chatId, CancellationToken cancellationToken)
    {
        return await store.LoadAsync<XyzzyGameState>(Key(chatId), cancellationToken)
               ?? new XyzzyGameState { ChatId = chatId };
    }

    public Task SaveAsync(XyzzyGameState game, CancellationToken cancellationToken) =>
        store.SaveAsync(Key(game.ChatId), game, cancellationToken);

    /// <summary>Every game that isn't Stopped - for XyzzyRoundReconciler to sweep on each scheduler
    /// tick. Assumes every "xyzzy:*:game"-shaped key is a XyzzyGameState - fine while this is the
    /// only key shape under the xyzzy: prefix; revisit the LIKE pattern if that changes.</summary>
    public async Task<IReadOnlyList<XyzzyGameState>> GetAllActiveAsync(CancellationToken cancellationToken)
    {
        var all = await store.LoadAllAsync<XyzzyGameState>("xyzzy:%:game", cancellationToken);
        return all.Where(g => g.Status is not XyzzyStatus.Stopped).ToList();
    }

    /// <summary>For ChatPurgeReconciler - legacy's mod_xyzzy_chatdata.isPurgable() only exists (and
    /// only ever blocks purge) once the module has actually been touched for a chat; GetAsync's own
    /// "?? new" default (StatusChangedUtc = "now") can't distinguish "never played" from "just
    /// played", which would otherwise make a chat that never touched xyzzy look artificially
    /// protected. This checks the raw stored value directly instead.</summary>
    public async Task<bool> ExistsAsync(long chatId, CancellationToken cancellationToken) =>
        await store.LoadAsync<XyzzyGameState>(Key(chatId), cancellationToken) is not null;

    public Task DeleteAsync(long chatId, CancellationToken cancellationToken) =>
        store.DeleteAsync(Key(chatId), cancellationToken);

    private static string Key(long chatId) => $"xyzzy:{chatId}:game";
}
