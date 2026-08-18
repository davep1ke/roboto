using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Steam.Commands;

/// <summary>Ports legacy mod_steam's /steam_remove - DM button-list picker, same shape as
/// xyzzy's Kick (XyzzySettingsCallbackHandler). See SteamRemovePlayerCallbackHandler for the tap
/// handling.</summary>
public sealed class SteamRemovePlayerCommand(SteamRepository steam, DmOutbox outbox) : IBotCommand
{
    public string Name => "steam_remove";
    public string Description => "Stops tracking a player's Steam achievements (asks over DM).";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Message.Chat.Type is ChatType.Private)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, "This only applies to group chats.", cancellationToken: cancellationToken);
            return;
        }

        var chatId = context.Message.Chat.Id;
        var chat = await steam.GetChatAsync(chatId, cancellationToken);
        var caller = context.Message.From!;

        if (chat.Players.Count == 0)
        {
            await context.Bot.SendMessage(chatId, "No players being tracked here.", cancellationToken: cancellationToken);
            return;
        }

        var keyboard = chat.Players
            .Select(p => new List<DmButton> { new(p.PlayerName, $"steam:rm:{chatId}:{p.SteamId}") })
            .Append([new DmButton("Cancel", $"steam:rm:{chatId}:cancel")])
            .ToList();

        var asked = await outbox.EnqueueButtonQuestionAsync(context.Bot, caller.Id, "Which player do you want to stop tracking?", keyboard, cancellationToken);
        if (!asked)
        {
            await context.Bot.SendMessage(chatId,
                $"{caller.FirstName} needs to open a private chat with me first.", cancellationToken: cancellationToken);
        }
    }
}
