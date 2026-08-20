using Roboto.Bot.Commands;
using Telegram.Bot;

namespace Roboto.Bot.Xyzzy.Commands;

/// <summary>
/// The "Change Packs" picker's message/keyboard building, shared between XyzzySettingsCallbackHandler
/// (reached via /xyzzy_settings) and XyzzyStartCommand (legacy also runs pack selection as one step
/// of the initial setup wizard, between Game Length and Timeout - see XyzzyImportMapper's own notes
/// on legacy's setPackFilter status). Every button this builds still routes through "xy:se:..."
/// callback data and XyzzySettingsCallbackHandler regardless of which caller sent the page, since
/// the underlying game/pack state is identical either way - only entry (who sends the first page)
/// and exit ("packsdone" branching on game.Status) differ per caller.
/// </summary>
internal static class XyzzyPackPickerUi
{
    public const int PacksPerPage = 30;

    private const string PackFiltersHeader = "The following packs (and their current status) are available. " +
        "You can toggle the packs using the keyboard below, or click 'Continue' to carry on. " +
        "You can also import packs from CRCast by clicking 'Import Pack'.";

    public static Task SendPageAsync(DmOutbox outbox, ITelegramBotClient bot, XyzzyGameState game, long userId, int page, CancellationToken cancellationToken) =>
        outbox.EnqueueButtonQuestionAsync(bot, userId, BuildMessage(game, page, out var clampedPage), BuildKeyboard(game, clampedPage), cancellationToken);

    public static string BuildMessage(XyzzyGameState game, int page, out int clampedPage)
    {
        var (pagePacks, totalPages) = PagePacks(game, page, out clampedPage);

        var active = pagePacks.Where(p => XyzzyPackFilter.IsEnabled(game, p.Id)).ToList();
        var inactive = pagePacks.Where(p => !XyzzyPackFilter.IsEnabled(game, p.Id)).ToList();

        // Plain text, not markdown-bold - DmOutbox doesn't carry a ParseMode through to the actual
        // send today (nothing in this codebase does yet), so "*Active Packs:*" would show up as
        // literal asterisks rather than bold.
        var message = PackFiltersHeader;
        if (active.Count > 0)
        {
            message += "\n\nActive Packs:\n" + string.Join('\n', active.Select(p => $"✅ {p.Name}"));
        }
        if (inactive.Count > 0)
        {
            message += "\n\nInactive Packs:\n" + string.Join('\n', inactive.Select(p => $"❌ {p.Name}"));
        }
        if (totalPages > 1)
        {
            message += $"\n\n(Page {clampedPage + 1} of {totalPages})";
        }

        return message;
    }

    public static List<List<DmButton>> BuildKeyboard(XyzzyGameState game, int page)
    {
        var (pagePacks, _) = PagePacks(game, page, out var clampedPage);

        var keyboard = new List<List<DmButton>>();
        foreach (var pack in pagePacks)
        {
            var enabled = XyzzyPackFilter.IsEnabled(game, pack.Id);
            var label = (enabled ? "✓ " : "") + pack.Name;
            keyboard.Add([new DmButton(label, $"xy:se:{game.ChatId}:packtoggle:{clampedPage}|{pack.Id}")]);
        }

        var totalPages = Math.Max(1, (CardCatalog.Packs.Count + PacksPerPage - 1) / PacksPerPage);
        var navRow = new List<DmButton>();
        if (clampedPage > 0)
        {
            navRow.Add(new DmButton("< Prev", $"xy:se:{game.ChatId}:packs:{clampedPage - 1}"));
        }
        if (clampedPage < totalPages - 1)
        {
            navRow.Add(new DmButton("Next >", $"xy:se:{game.ChatId}:packs:{clampedPage + 1}"));
        }
        if (navRow.Count > 0)
        {
            keyboard.Add(navRow);
        }

        keyboard.Add([new DmButton("Import Pack", $"xy:se:{game.ChatId}:packsimport:{clampedPage}")]);
        keyboard.Add([new DmButton("All Packs", $"xy:se:{game.ChatId}:packsall:{clampedPage}")]);
        keyboard.Add([new DmButton("Reset to Base Pack", $"xy:se:{game.ChatId}:packsreset:{clampedPage}")]);
        keyboard.Add([new DmButton("Continue", $"xy:se:{game.ChatId}:packsdone:_")]);
        return keyboard;
    }

    /// <summary>Packs sorted enabled-first-then-name (legacy's own ordering - a real win once a chat
    /// has hundreds/thousands of packs and only a handful enabled), sliced to one page.</summary>
    private static (List<XyzzyPack> PagePacks, int TotalPages) PagePacks(XyzzyGameState game, int page, out int clampedPage)
    {
        var ordered = CardCatalog.Packs
            .OrderByDescending(p => XyzzyPackFilter.IsEnabled(game, p.Id))
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        var totalPages = Math.Max(1, (ordered.Count + PacksPerPage - 1) / PacksPerPage);
        clampedPage = Math.Clamp(page, 0, totalPages - 1);
        return (ordered.Skip(clampedPage * PacksPerPage).Take(PacksPerPage).ToList(), totalPages);
    }
}
