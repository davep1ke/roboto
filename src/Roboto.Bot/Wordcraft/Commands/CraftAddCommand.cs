using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Wordcraft.Commands;

/// <summary>
/// Ports legacy mod_wordcraft's /craft_add - a single DM question (see phase 9's design call:
/// every multi-step/DM-based flow in this codebase goes through DmOutbox/ReplyRouter now, not a
/// group-posted question the way legacy asked it). The word list itself stays global, but the
/// confirmation is posted back to the group the command was triggered from, matching legacy.
///
/// Resolves ReplyRouter lazily via IServiceProvider rather than as a constructor dependency - see
/// the warning in ReplyRouter's own doc comment for why a direct dependency here would be circular.
/// </summary>
public sealed class CraftAddCommand(IServiceProvider services, WordcraftStore store) : IReplyHandler
{
    public string Name => "craft_add";
    public string Description => "Adds a word to the /craft word list (asks over DM).";

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
            "Enter the word to add", cancellationToken);

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
        words.Add(word);
        await store.SaveWordsAsync(words, cancellationToken);
        await bot.SendMessage(pending.TargetChatId, $"Added {word} for {reply.From!.FirstName}", cancellationToken: cancellationToken);
    }
}
