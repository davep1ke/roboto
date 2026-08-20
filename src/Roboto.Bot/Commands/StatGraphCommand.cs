using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Roboto.Bot.Stats;
using ScottPlot;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Roboto.Bot.Commands;

/// <summary>
/// Charts recorded stats' bucketed history (StatsRecorder.RecordAtAsync's 15-min/48h Buckets, not
/// the all-time Total) as an image. Restores legacy's own /statgraph flexibility - each argument is
/// a regex matched against a series name (not just an exact name), multiple series overlay on one
/// chart - which the rewrite's first pass (10b) had dropped down to a single exact-name lookup.
///
/// Deliberately not a port of legacy's WinForms Chart rendering (1200x600 JPEG, one hardcoded
/// pastel-blue plot area, default column/line styling with no palette design) - full creative
/// freedom on the visual redesign, per the user's own explicit go-ahead. Cumulative series render
/// with a filled area (they're interval counts, closer to a histogram than a gauge reading);
/// Snapshot series render as a plain line. A stat's color is a stable hash of its name, so the same
/// stat is always the same color across renders/messages.
/// </summary>
public sealed class StatGraphCommand(StatsRecorder stats, IOptions<BotOptions> options) : IBotCommand
{
    private const int MaxSeries = 8;

    private static readonly string[] Palette =
    [
        "#4C72B0", "#DD8452", "#55A868", "#C44E52", "#8172B2", "#937860", "#DA8BC3", "#8C8C8C",
    ];

    public string Name => "statgraph";
    public string Description => "Charts recorded stats' history as an image. Usage: /statgraph <name-or-regex> [name-or-regex ...]";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var chatId = context.Message.Chat.Id;
        var all = await stats.GetAllAsync(cancellationToken);

        if (context.Args.Length == 0)
        {
            await context.Bot.SendMessage(chatId,
                $"Usage: /statgraph <name-or-regex> [name-or-regex ...]\n\n{AvailableStatsText(all)}", cancellationToken: cancellationToken);
            return;
        }

        // Legacy split multiple selectors on "|" as well as whitespace (each command arg here is
        // already whitespace-split by CommandRouter) and matched each as a regex against the series
        // name - restoring that lets one call chart several related stats together.
        var patterns = context.Args.SelectMany(a => a.Split('|', StringSplitOptions.RemoveEmptyEntries)).ToList();
        var matched = all
            .Where(s => patterns.Any(p => SafeIsMatch(s.Name, p)))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matched.Count == 0)
        {
            var requested = string.Join(", ", context.Args);
            await context.Bot.SendMessage(chatId,
                $"No recorded history for '{requested}'.\n\n{AvailableStatsText(all)}", cancellationToken: cancellationToken);
            return;
        }

        var shown = matched.Take(MaxSeries).ToList();
        var plot = BuildPlot(shown);

        var bytes = plot.GetImageBytes(1200, 600, ImageFormat.Png);
        using var stream = new MemoryStream(bytes);
        var caption = shown.Count == 1 ? shown[0].Name : string.Join(", ", shown.Select(s => s.Name));
        var fileName = shown.Count == 1 ? $"{shown[0].Name}.png" : "statgraph.png";
        await context.Bot.SendPhoto(chatId, InputFile.FromStream(stream, fileName), caption: caption, cancellationToken: cancellationToken);
    }

    private static bool SafeIsMatch(string name, string pattern)
    {
        try
        {
            return Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase);
        }
        catch (ArgumentException)
        {
            // Not a valid regex (e.g. an unbalanced bracket typed by hand) - fall back to a plain
            // substring match rather than rejecting the whole command.
            return name.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string AvailableStatsText(IReadOnlyList<StatSeries> all) =>
        all.Count == 0 ? "No stats recorded yet." : "Available stats:\n" + string.Join('\n', all.OrderBy(s => s.Name).Select(s => s.Name));

    private Plot BuildPlot(IReadOnlyList<StatSeries> series)
    {
        var plot = new Plot();

        var now = DateTime.UtcNow;
        var windowStart = StatsRecorder.FloorToBucket(now) - StatsRecorder.BucketSize * (StatsRecorder.MaxBuckets - 1);
        var slotStarts = Enumerable.Range(0, StatsRecorder.MaxBuckets).Select(i => windowStart + StatsRecorder.BucketSize * i).ToArray();
        var xs = slotStarts.Select(s => s.ToOADate()).ToArray();

        foreach (var s in series)
        {
            var ys = Densify(s, slotStarts);
            var scatter = plot.Add.Scatter(xs, ys);
            scatter.LegendText = s.Name;
            scatter.MarkerSize = 0;
            scatter.LineWidth = 2;

            var color = ScottPlot.Color.FromHex(Palette[(uint)s.Name.GetHashCode() % Palette.Length]);
            scatter.Color = color;

            if (s.Mode == StatMode.Cumulative)
            {
                scatter.FillY = true;
                scatter.FillYColor = color.WithAlpha(0.18);
            }
        }

        plot.Axes.DateTimeTicksBottom();
        plot.Title($"{options.Value.BotUsername} statistics - last 48h");
        plot.YLabel($"Value / {StatsRecorder.BucketSize.TotalMinutes:0} mins");
        plot.ShowLegend();

        return plot;
    }

    /// <summary>Turns a sparse bucket list (only intervals that actually got a value exist) into a
    /// continuous 48h series for plotting. Cumulative gaps fill with 0 (nothing happened that
    /// interval); Snapshot gaps carry forward the last known value (a gauge that wasn't re-recorded
    /// hasn't changed); everything before the series' first-ever sample is NaN so ScottPlot renders
    /// a true gap rather than a misleading flat line/zero baseline predating the stat's existence.</summary>
    private static double[] Densify(StatSeries series, DateTime[] slotStarts)
    {
        var byStart = series.Buckets.ToDictionary(b => b.StartUtc, b => b.Value);
        var ys = new double[slotStarts.Length];
        var lastKnown = double.NaN;
        var seenAny = false;

        for (var i = 0; i < slotStarts.Length; i++)
        {
            if (byStart.TryGetValue(slotStarts[i], out var value))
            {
                ys[i] = value;
                lastKnown = value;
                seenAny = true;
            }
            else if (!seenAny)
            {
                ys[i] = double.NaN;
            }
            else
            {
                ys[i] = series.Mode == StatMode.Snapshot ? lastKnown : 0;
            }
        }

        return ys;
    }
}
