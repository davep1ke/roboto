using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Wordcraft.Commands;

/// <summary>Ports legacy mod_wordcraft's /craft_remove - same shape as CraftAddCommand, see its
/// doc comment.</summary>
public sealed class CraftRemoveCommand(IServiceProvider services, WordcraftStore store) : IReplyHandler
{
    public string Name => "craft_remove";
    public string Description => "Removes a word from the /craft word list (asks over DM).";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Message.Chat.Type is ChatType.Private)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, "This only applies to group chats.", cancellationToken: cancellationToken);
            return;
        }

        var replies = services.GetRequiredService<ReplyRouter>();
        var userId = context.Message.From!.Id;
        var asked = await replies.AskAsync(context.Bot, context.Message.Chat.Id, userId, Name, "await-word", data: null,
            "Enter the word to remove", cancellationToken);

        if (!asked)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id,
                $"{context.Message.From.FirstName} needs to open a private chat with me first.", cancellationToken: cancellationToken);
        }
    }

    public async Task HandleReplyAsync(ITelegramBotClient bot, PendingReply pending, Message reply, CancellationToken cancellationToken)
    {
        var word = reply.Text!.Trim();
        var words = await store.GetWordsAsync(cancellationToken);
        var success = words.Remove(word);
        if (success)
        {
            await store.SaveWordsAsync(words, cancellationToken);
        }

        await bot.SendMessage(pending.TargetChatId,
            $"Removed {word} for {reply.From!.FirstName} " + (success ? "successfully" : "but fell on my ass"),
            cancellationToken: cancellationToken);
    }
}
