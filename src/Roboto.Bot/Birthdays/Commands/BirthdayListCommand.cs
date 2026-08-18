using Roboto.Bot.Commands;
using Telegram.Bot;

namespace Roboto.Bot.Birthdays.Commands;

/// <summary>Ports legacy mod_birthdays' /birthday_list - no reply needed, posts straight to the
/// group, sorted by day-of-year (matches legacy's own sort so birthdays show in calendar order
/// regardless of the year each was originally entered with).</summary>
public sealed class BirthdayListCommand(BirthdaysRepository birthdays) : IBotCommand
{
    public string Name => "birthday_list";
    public string Description => "Shows the list of birthdays that have been added.";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var chat = await birthdays.GetAsync(context.Message.Chat.Id, cancellationToken);

        var message = "I know about the following birthdays!";
        foreach (var b in chat.Birthdays.OrderBy(b => b.Birthday.Subtract(new DateTime(b.Birthday.Year, 1, 1))))
        {
            message += $"\n{b.Birthday:yyyy-MM-dd}\t\t - *{b.Name}*";
        }

        await context.Bot.SendMessage(context.Message.Chat.Id, message, cancellationToken: cancellationToken);
    }
}
