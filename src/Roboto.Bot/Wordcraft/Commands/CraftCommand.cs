using Roboto.Bot.Commands;
using Telegram.Bot;

namespace Roboto.Bot.Wordcraft.Commands;

/// <summary>
/// Ports legacy mod_wordcraft's /craft: picks 1-4 random words (loop runs a fixed number of times;
/// a duplicate pick is just skipped, not retried, so the final phrase can end up shorter than the
/// roll - matches legacy's own loop shape exactly), then ~20% of the time tacks on a chained
/// number suffix (20% for a first digit, then 10% for a second, then 20%/70% for trailing zeroes -
/// an odd chain of rolls, ported as-is rather than "fixed" into something more sensible).
/// </summary>
public sealed class CraftCommand(WordcraftStore store) : IBotCommand
{
    public string Name => "craft";
    public string Description => "Crafts a random phrase badger.";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var words = await store.GetWordsAsync(cancellationToken);
        await context.Bot.SendMessage(context.Message.Chat.Id, CraftPhrase(words), cancellationToken: cancellationToken);
    }

    private static string CraftPhrase(List<string> words)
    {
        var picked = new List<string>();
        var wordCount = Random.Shared.Next(4) + 1;
        for (var i = 0; i < wordCount; i++)
        {
            var word = words[Random.Shared.Next(words.Count)];
            if (!picked.Contains(word))
            {
                picked.Add(word);
            }
        }

        var result = string.Join(" ", picked);

        if (Random.Shared.Next(100) < 20)
        {
            result += " " + Random.Shared.Next(9);
            if (Random.Shared.Next(100) < 10)
            {
                result += Random.Shared.Next(9);
            }

            if (Random.Shared.Next(100) < 20)
            {
                result += "0";
                if (Random.Shared.Next(100) < 70)
                {
                    result += "0";
                }
            }
        }

        return result;
    }
}
