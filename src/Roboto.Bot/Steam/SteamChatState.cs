namespace Roboto.Bot.Steam;

/// <summary>
/// Per-chat mod_steam state - own key/POCO via IStateStore, per ChatState's own doc comment about
/// module-owned per-chat data (same pattern XyzzyGameState/BirthdayChatState/QuoteChatState
/// follow). SteamCoreState (the achievement-schema cache) is the module's one piece of global data.
/// </summary>
public sealed class SteamChatState
{
    public long ChatId { get; set; }
    public List<SteamPlayer> Players { get; set; } = [];
}
