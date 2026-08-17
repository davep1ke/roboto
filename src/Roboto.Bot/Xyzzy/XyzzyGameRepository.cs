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

    private static string Key(long chatId) => $"xyzzy:{chatId}:game";
}
