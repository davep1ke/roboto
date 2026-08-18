using Roboto.Bot.Persistence;

namespace Roboto.Bot.Wordcraft;

/// <summary>
/// Ports legacy mod_wordcraft's single global (not per-chat) word list - one shared vocabulary
/// across every chat the bot's in, same as legacy. Seeded with legacy's own default 8 words on
/// first use.
/// </summary>
public sealed class WordcraftStore(IStateStore store)
{
    private const string Key = "wordcraft:words";

    private static readonly List<string> DefaultWords =
        ["Bilge", "Rabbit", "Moose", "Ramp", "Clown", "Glimp", "Hop", "Mop"];

    public async Task<List<string>> GetWordsAsync(CancellationToken cancellationToken) =>
        await store.LoadAsync<List<string>>(Key, cancellationToken) ?? [..DefaultWords];

    public Task SaveWordsAsync(List<string> words, CancellationToken cancellationToken) =>
        store.SaveAsync(Key, words, cancellationToken);
}
