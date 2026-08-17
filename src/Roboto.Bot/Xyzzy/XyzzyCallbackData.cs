namespace Roboto.Bot.Xyzzy;

/// <summary>
/// Encodes/decodes this module's inline-keyboard callback_data: "xy:&lt;action&gt;:&lt;groupChatId&gt;:
/// &lt;round&gt;:&lt;cardId&gt;". Cards are offered over DM, but a DM callback query alone can't say which
/// game it's about - legacy solved the same problem by round-tripping the game's chat ID through
/// its ExpectedReply record; this does the equivalent by putting it directly in the button. Round
/// number is a staleness guard: a tap against a message from a round that's already moved on gets
/// rejected with a clear "that round's over" instead of corrupting current state. Comfortably under
/// Telegram's 64-byte callback_data limit even for large negative supergroup chat IDs.
/// </summary>
public readonly record struct XyzzyCallbackData(string Action, long ChatId, int Round, string CardId)
{
    public string Encode() => $"xy:{Action}:{ChatId}:{Round}:{CardId}";

    public static bool TryParse(string data, out XyzzyCallbackData result)
    {
        var parts = data.Split(':', 5);
        if (parts.Length == 5 && parts[0] == "xy" && long.TryParse(parts[2], out var chatId) && int.TryParse(parts[3], out var round))
        {
            result = new XyzzyCallbackData(parts[1], chatId, round, parts[4]);
            return true;
        }

        result = default;
        return false;
    }
}
