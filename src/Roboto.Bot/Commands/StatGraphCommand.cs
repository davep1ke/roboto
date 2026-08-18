using Roboto.Bot.Stats;
using ScottPlot;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Roboto.Bot.Commands;

/// <summary>
/// Phase 10b - charts a recorded StatSeries' RecentPoints (10a's StatsRecorder, already bounded to
/// MaxRecentPoints) as a line chart, sent as a photo. Deliberately just a rendering layer on top of
/// 10a's data collection - no new data is gathered here.
///
/// Renders via ScottPlot 5, which uses SkiaSharp internally (SkiaSharp.NativeAssets.Linux.
/// NoDependencies - statically linked, no libfontconfig1/etc needed in the runtime image, verified
/// by an actual `docker compose build` + a real /statgraph round-trip against the built image, not
/// just a local build).
/// </summary>
public sealed class StatGraphCommand(StatsRecorder stats) : IBotCommand
{
    public string Name => "statgraph";
    public string Description => "Charts a recorded stat's recent history as an image. Usage: /statgraph <name>";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var chatId = context.Message.Chat.Id;
        var all = await stats.GetAllAsync(cancellationToken);

        var requestedName = context.Args.Length > 0 ? context.Args[0] : null;
        var series = requestedName is null
            ? null
            : all.FirstOrDefault(s => string.Equals(s.Name, requestedName, StringComparison.OrdinalIgnoreCase));

        if (series is null || series.RecentPoints.Count == 0)
        {
            var available = all.Count == 0 ? "No stats recorded yet." : string.Join('\n', all.OrderBy(s => s.Name).Select(s => s.Name));
            var prefix = requestedName is null ? "Usage: /statgraph <name>" : $"No recorded history for '{requestedName}'.";
            await context.Bot.SendMessage(chatId, $"{prefix}\n\nAvailable stats:\n{available}", cancellationToken: cancellationToken);
            return;
        }

        var plot = new Plot();
        var xs = series.RecentPoints.Select(p => p.Utc.ToOADate()).ToArray();
        var ys = series.RecentPoints.Select(p => p.Value).ToArray();
        plot.Add.Scatter(xs, ys);
        plot.Axes.DateTimeTicksBottom();
        plot.Title(series.Name);

        var bytes = plot.GetImageBytes(800, 400, ImageFormat.Png);
        using var stream = new MemoryStream(bytes);
        await context.Bot.SendPhoto(chatId, InputFile.FromStream(stream, $"{series.Name}.png"), caption: series.Name, cancellationToken: cancellationToken);
    }
}
