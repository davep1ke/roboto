using Roboto.Bot.Commands;
using Telegram.Bot;

namespace Roboto.Bot.Quotes.Commands;

/// <summary>Ports legacy mod_quote's /quote - picks a random quote from this chat's database.</summary>
public sealed class QuoteCommand(QuotesRepository quotes) : IBotCommand
{
    public string Name => "quote";
    public string Description => "Picks a random quote from this chat's database.";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var chat = await quotes.GetAsync(context.Message.Chat.Id, cancellationToken);
        var text = chat.Quotes.Count > 0
            ? chat.Quotes[Random.Shared.Next(chat.Quotes.Count)].GetText()
            : "No quotes in DB";

        await context.Bot.SendMessage(context.Message.Chat.Id, text, cancellationToken: cancellationToken);
    }
}
