using Roboto.Bot.Commands;
using Telegram.Bot;

namespace Roboto.Bot.Steam.Commands;

/// <summary>Ports legacy mod_steam's /steam_stats - no reply needed, posts straight to the group.</summary>
public sealed class SteamStatsCommand(SteamRepository steam) : IBotCommand
{
    public string Name => "steam_stats";
    public string Description => "Shows Steam achievement tracking status for this chat.";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var chatId = context.Message.Chat.Id;
        var chat = await steam.GetChatAsync(chatId, cancellationToken);
        var core = await steam.GetCoreAsync(cancellationToken);

        var announce = "Currently watching achievements from the following players: \n";
        foreach (var player in chat.Players)
        {
            announce += $"*{player.PlayerName}* - {player.Chievs.Count} known achievements\n";
        }

        var achievements = core.Games.Sum(g => g.Achievements.Count);
        announce += $"Tracking {achievements} achievements across {core.Games.Count} games";

        await context.Bot.SendMessage(chatId, announce, cancellationToken: cancellationToken);
    }
}
