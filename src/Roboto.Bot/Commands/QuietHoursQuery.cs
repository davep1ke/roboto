using Roboto.Bot.Persistence;

namespace Roboto.Bot.Commands;

/// <summary>
/// Read-only query for a chat's quiet-hours setting (written by SetQuietHoursCommand) - lets other
/// code check "is now quiet for this chat" without depending on SetQuietHoursCommand itself as an
/// IBotCommand. Added for mod_xyzzy's round scheduler (phase 8.3), mirroring legacy's cross-module
/// mod_standard.isTimeInQuietPeriod call.
/// </summary>
public sealed class QuietHoursQuery(IStateStore store)
{
    /// <summary>`now` is an optional override (defaults to the real current time) purely so tests
    /// can exercise the overnight-wraparound branch (e.g. 22:00-06:00) deterministically without a
    /// full clock-abstraction refactor - production callers never pass it.</summary>
    public async Task<bool> IsQuietNowAsync(long chatId, CancellationToken cancellationToken, TimeSpan? now = null)
    {
        var hours = await store.LoadAsync<QuietHours>(SetQuietHoursCommand.QuietHoursKey(chatId), cancellationToken);
        if (hours is null)
        {
            return false;
        }

        var time = now ?? DateTime.UtcNow.TimeOfDay;
        return hours.Start <= hours.End
            ? time >= hours.Start && time < hours.End
            : time >= hours.Start || time < hours.End; // wraps past midnight
    }
}
