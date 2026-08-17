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

    private static string Key(long chatId) => $"xyzzy:{chatId}:game";
}
