using Microsoft.Extensions.Options;
using Roboto.Bot.Commands;
using Telegram.Bot;

namespace Roboto.Bot.Steam.Commands;

/// <summary>Ports legacy mod_steam's /steam_check - manually checks this chat's tracked players
/// right now, rather than waiting for the next scheduler tick. Undocumented in legacy's own
/// getMethodDescriptions() but present in chatEvent - kept here since it's cheap and genuinely
/// useful for testing/demonstration without waiting 15 minutes.</summary>
public sealed class SteamCheckCommand(SteamReconciler reconciler, SteamRepository steam, IOptions<BotOptions> options) : IBotCommand
{
    public string Name => "steam_check";
    public string Description => "Manually checks tracked players here for new Steam achievements right now.";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var apiKey = options.Value.SteamApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            await context.Bot.SendMessage(context.Message.Chat.Id,
                "Steam achievement tracking isn't configured for this bot.", cancellationToken: cancellationToken);
            return;
        }

        var chat = await steam.GetChatAsync(context.Message.Chat.Id, cancellationToken);
        await reconciler.ReconcileChatAsync(context.Bot, apiKey, chat, cancellationToken);
    }
}
