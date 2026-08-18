using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Steam.Commands;

/// <summary>
/// Ports legacy mod_steam's /steam_addplayer - a single DM question (steamID64), validated against
/// the Steam API before being added (rejects private profiles, same as legacy). Matches phase 9's
/// design call: DM/DmOutbox, not a group-posted question the way legacy asked it.
///
/// Resolves ReplyRouter lazily via IServiceProvider rather than as a constructor dependency - see
/// the warning in ReplyRouter's own doc comment for why a direct dependency here would be circular.
/// </summary>
public sealed class SteamAddPlayerCommand(IServiceProvider services, SteamApiClient api, SteamRepository steam, IOptions<BotOptions> options) : IReplyHandler
{
    private const string AwaitSteamId = "await-steamid";

    public string Name => "steam_addplayer";
    public string Description => "Adds a player to track Steam achievements for (asks over DM).";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Message.Chat.Type is ChatType.Private)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, "This only applies to group chats.", cancellationToken: cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(options.Value.SteamApiKey))
        {
            await context.Bot.SendMessage(context.Message.Chat.Id,
                "Steam achievement tracking isn't configured for this bot.", cancellationToken: cancellationToken);
            return;
        }

        var replies = services.GetRequiredService<ReplyRouter>();
        var userId = context.Message.From!.Id;
        var asked = await replies.AskAsync(context.Bot, context.Message.Chat.Id, userId, Name, AwaitSteamId, data: null,
            "Enter the steamID of the player you want to add. /steam_help to find out how to get this.", cancellationToken);

        if (!asked)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id,
                $"{context.Message.From.FirstName} needs to open a private chat with me first.", cancellationToken: cancellationToken);
        }
    }

    public async Task HandleReplyAsync(ITelegramBotClient bot, PendingReply pending, Message reply, CancellationToken cancellationToken)
    {
        var text = reply.Text!.Trim();

        if (text.Equals("cancel", StringComparison.OrdinalIgnoreCase))
        {
            await bot.SendMessage(pending.UserId, "Cancelled.", cancellationToken: cancellationToken);
            return;
        }

        if (!long.TryParse(text, out _))
        {
            var replies = services.GetRequiredService<ReplyRouter>();
            await replies.AskAsync(bot, pending.TargetChatId, pending.UserId, Name, AwaitSteamId, data: null,
                $"{text} is not a valid playerID. Enter a valid playerID or 'Cancel'", cancellationToken);
            return;
        }

        var summary = await api.GetPlayerSummaryAsync(options.Value.SteamApiKey, text, cancellationToken);
        if (summary is null)
        {
            var replies = services.GetRequiredService<ReplyRouter>();
            await replies.AskAsync(bot, pending.TargetChatId, pending.UserId, Name, AwaitSteamId, data: null,
                $"{text} is not a valid playerID. Enter a valid playerID or 'Cancel'", cancellationToken);
            return;
        }

        if (summary.IsPrivate)
        {
            await bot.SendMessage(pending.TargetChatId,
                $"Couldn't add {summary.PersonaName} as their profile is set to private", cancellationToken: cancellationToken);
            return;
        }

        var chat = await steam.GetChatAsync(pending.TargetChatId, cancellationToken);
        chat.Players.Add(new SteamPlayer { SteamId = text, PlayerName = summary.PersonaName });
        await steam.SaveChatAsync(chat, cancellationToken);
        await bot.SendMessage(pending.TargetChatId,
            $"Added {summary.PersonaName}. Any steam achievements will be announced.", cancellationToken: cancellationToken);
    }
}
