using Roboto.Bot.Commands;
using Telegram.Bot;

namespace Roboto.Bot.Steam.Commands;

/// <summary>Ports legacy mod_steam's /steam_help - static help text, no state involved.</summary>
public sealed class SteamHelpCommand : IBotCommand
{
    public string Name => "steam_help";
    public string Description => "Explains how to find your Steam playerID.";

    public Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken) =>
        context.Bot.SendMessage(context.Message.Chat.Id,
            "You are looking for an ID from the Steam Community site, try http://steamcommunity.com/ and find your profile. " +
            "You should have something like http://steamcommunity.com/profiles/01234567890132456 . Take this number on the end of the URL.",
            cancellationToken: cancellationToken);
}
