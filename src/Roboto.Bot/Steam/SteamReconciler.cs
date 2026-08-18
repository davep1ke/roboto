using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Roboto.Bot.Steam;

/// <summary>
/// Ports legacy mod_steam_player.checkAchievements()/mod_steam.backgroundProcessing() - for every
/// tracked player in every chat, checks their recently-played games for newly-earned achievements
/// (not already recorded against that player) and announces them to the chat, batching the
/// message the same way legacy did (first 5, then "and (N) others"). No-ops entirely - logs once,
/// does nothing - when BotOptions.SteamApiKey is blank, since most instances won't set one.
///
/// Legacy put this logic directly on the data class (mod_steam_player itself made the API calls) -
/// kept as a plain POCO here instead (SteamPlayer), with the behavior living in this service,
/// matching every other module's split (e.g. XyzzyPlayer/XyzzyRoundService).
/// </summary>
public sealed class SteamReconciler(SteamApiClient api, SteamRepository steam, IOptions<BotOptions> options, ILogger<SteamReconciler> logger)
{
    private bool _loggedNoKey;

    public async Task ReconcileAllAsync(ITelegramBotClient bot, CancellationToken cancellationToken)
    {
        var apiKey = options.Value.SteamApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (!_loggedNoKey)
            {
                logger.LogInformation("mod_steam: SteamApiKey not configured - achievement checks disabled");
                _loggedNoKey = true;
            }

            return;
        }

        foreach (var chat in await steam.GetAllChatsAsync(cancellationToken))
        {
            try
            {
                await ReconcileChatAsync(bot, apiKey, chat, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reconciling mod_steam achievements for chat {ChatId} failed", chat.ChatId);
            }
        }
    }

    /// <summary>Public so /steam_check (a manual, single-chat trigger - matches legacy's own
    /// checkChat(c) scope, not a global sweep) can reuse the same per-chat logic the scheduler
    /// tick uses.</summary>
    public async Task ReconcileChatAsync(ITelegramBotClient bot, string apiKey, SteamChatState chat, CancellationToken cancellationToken)
    {
        foreach (var player in chat.Players)
        {
            try
            {
                await CheckPlayerAsync(bot, apiKey, chat.ChatId, player, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Checking Steam achievements for player {PlayerName} in chat {ChatId} failed", player.PlayerName, chat.ChatId);
            }
        }

        await steam.SaveChatAsync(chat, cancellationToken);
    }

    private async Task CheckPlayerAsync(ITelegramBotClient bot, string apiKey, long chatId, SteamPlayer player, CancellationToken cancellationToken)
    {
        player.LastChecked = DateTime.UtcNow;
        var core = await steam.GetCoreAsync(cancellationToken);
        var announcements = new List<string>();

        foreach (var recentGame in await api.GetRecentlyPlayedGamesAsync(apiKey, player.SteamId, cancellationToken))
        {
            var gameId = recentGame.AppId.ToString();
            var isNewGame = core.FindGame(gameId) is null;
            var game = core.FindGame(gameId) ?? new SteamGame { GameId = gameId, DisplayName = recentGame.Name };

            var achievedCodes = await api.GetAchievedCodesAsync(apiKey, player.SteamId, gameId, cancellationToken);
            var newCodes = achievedCodes.Where(code => !player.Chievs.Any(c => c.ChievCode == code && c.AppId == gameId)).ToList();
            if (newCodes.Count == 0)
            {
                continue;
            }

            var unresolved = new List<string>();
            foreach (var code in newCodes)
            {
                player.Chievs.Add(new SteamChiev { ChievCode = code, AppId = gameId });
                var schema = game.FindAchievement(code);
                if (schema is null)
                {
                    unresolved.Add(code);
                }
                else
                {
                    announcements.Add($"{schema} in {game.DisplayName}");
                }
            }

            if (unresolved.Count > 0)
            {
                game.Achievements = await api.GetGameSchemaAsync(apiKey, gameId, cancellationToken);
                foreach (var code in unresolved)
                {
                    var schema = game.FindAchievement(code);
                    announcements.Add(schema is null ? $"{code.Replace('_', ' ')} in {game.DisplayName}" : $"{schema} in {game.DisplayName}");
                }
            }

            if (isNewGame)
            {
                core.Games.Add(game);
            }
        }

        await steam.SaveCoreAsync(core, cancellationToken);

        if (announcements.Count == 0)
        {
            return;
        }

        var message = $"{player.PlayerName} got the following achievements:\n";
        var shown = Math.Min(5, announcements.Count);
        for (var i = 0; i < shown; i++)
        {
            message += $"- {announcements[i]}\n";
        }

        if (announcements.Count > 5)
        {
            message += $"And ({announcements.Count - 5}) others";
        }

        await bot.SendMessage(chatId, message, cancellationToken: cancellationToken);
    }
}
