using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Commands;

namespace Roboto.Bot.Tests;

public class QuietHoursQueryTests
{
    private const long ChatId = -400;

    [Fact]
    public async Task NoQuietHoursSetIsNeverQuiet()
    {
        using var bot = new TestBot();
        var query = bot.Services.GetRequiredService<QuietHoursQuery>();

        Assert.False(await query.IsQuietNowAsync(ChatId, CancellationToken.None));
    }

    [Fact]
    public async Task StraightWindowIsQuietOnlyBetweenStartAndEnd()
    {
        using var bot = await SetQuietHoursAsync(TimeSpan.FromHours(22), TimeSpan.FromHours(23));
        var query = bot.Services.GetRequiredService<QuietHoursQuery>();

        Assert.True(await query.IsQuietNowAsync(ChatId, CancellationToken.None, now: TimeSpan.FromHours(22.5)));
        Assert.False(await query.IsQuietNowAsync(ChatId, CancellationToken.None, now: TimeSpan.FromHours(21)));
        Assert.False(await query.IsQuietNowAsync(ChatId, CancellationToken.None, now: TimeSpan.FromHours(23.5)));
    }

    [Fact]
    public async Task OvernightWindowWrapsPastMidnight()
    {
        using var bot = await SetQuietHoursAsync(TimeSpan.FromHours(22), TimeSpan.FromHours(6));
        var query = bot.Services.GetRequiredService<QuietHoursQuery>();

        Assert.True(await query.IsQuietNowAsync(ChatId, CancellationToken.None, now: TimeSpan.FromHours(23)));
        Assert.True(await query.IsQuietNowAsync(ChatId, CancellationToken.None, now: TimeSpan.FromHours(2)));
        Assert.False(await query.IsQuietNowAsync(ChatId, CancellationToken.None, now: TimeSpan.FromHours(12)));
    }

    private static async Task<TestBot> SetQuietHoursAsync(TimeSpan start, TimeSpan end)
    {
        var bot = new TestBot();

        // Drive the real /setquiethours DM flow rather than poking IStateStore directly, so this
        // also doubles as a regression check that SetQuietHoursCommand still writes the key
        // QuietHoursQuery reads.
        await bot.SendAsync(TestBot.GroupMessage(ChatId, 1, "/setquiethours"));
        await bot.SendAsync(TestBot.PrivateMessage(1, start.ToString()));
        await bot.SendAsync(TestBot.PrivateMessage(1, end.ToString()));
        return bot;
    }
}
