using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Xyzzy;

namespace Roboto.Bot.Tests.Xyzzy;

/// <summary>Covers /xyzzy_leave's DM variant (XyzzyLeaveCommand.ExecuteDmPickerAsync +
/// XyzzyLeavePickerCallbackHandler) - typed with no chat context, scans every active game the
/// caller is in and shows a "which game?" picker, restoring a legacy feature the rewrite had
/// deliberately cut for v1.</summary>
public class XyzzyLeaveDmVariantTests
{
    private const long ChatOne = -601;
    private const long ChatTwo = -602;
    private const long Alice = 1;
    private const long Bob = 2;
    private const long Carol = 3;

    private static async Task StartThreePlayerGameAsync(TestBot bot, long chatId)
    {
        await bot.SendAsync(TestBot.GroupMessage(chatId, Alice, "/xyzzy_start", firstName: "Alice"));
        var choiceMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
        await bot.SendCallbackAsync(Alice, choiceMessage.Buttons!.First(b => b.Text == "Use Defaults"));
        await bot.SendAsync(TestBot.GroupMessage(chatId, Bob, "/xyzzy_join", firstName: "Bob"));
        await bot.SendAsync(TestBot.GroupMessage(chatId, Carol, "/xyzzy_join", firstName: "Carol"));
        var startMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 } && m.Buttons.Any(b => b.Text == "Start"));
        await bot.SendCallbackAsync(Alice, startMessage.Buttons!.First(b => b.Text == "Start"));
    }

    [Fact]
    public async Task PickerListsEveryActiveGameTheCallerIsIn()
    {
        using var bot = new TestBot();
        await StartThreePlayerGameAsync(bot, ChatOne);
        await StartThreePlayerGameAsync(bot, ChatTwo);

        await bot.SendAsync(TestBot.PrivateMessage(Alice, "/xyzzy_leave"));

        var picker = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
        Assert.Contains("Which game", picker.Text);
        Assert.Contains(picker.Buttons!, b => b.CallbackData == $"xy:lv:{ChatOne}");
        Assert.Contains(picker.Buttons!, b => b.CallbackData == $"xy:lv:{ChatTwo}");
        Assert.Contains(picker.Buttons!, b => b.Text == "Cancel");
    }

    [Fact]
    public async Task PickingAGameRemovesTheCallerFromOnlyThatOne()
    {
        using var bot = new TestBot();
        await StartThreePlayerGameAsync(bot, ChatOne);
        await StartThreePlayerGameAsync(bot, ChatTwo);

        await bot.SendAsync(TestBot.PrivateMessage(Alice, "/xyzzy_leave"));
        var picker = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
        await bot.SendCallbackAsync(Alice, picker.Buttons!.First(b => b.CallbackData == $"xy:lv:{ChatOne}"));

        var games = bot.Services.GetRequiredService<XyzzyGameRepository>();
        var gameOne = await games.GetAsync(ChatOne, CancellationToken.None);
        var gameTwo = await games.GetAsync(ChatTwo, CancellationToken.None);

        Assert.DoesNotContain(gameOne.Players, p => p.PlayerId == Alice);
        Assert.Contains(gameTwo.Players, p => p.PlayerId == Alice);
        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatOne && m.Text.Contains("left the game"));
    }

    [Fact]
    public async Task CancelLeavesEverythingUnchanged()
    {
        using var bot = new TestBot();
        await StartThreePlayerGameAsync(bot, ChatOne);

        await bot.SendAsync(TestBot.PrivateMessage(Alice, "/xyzzy_leave"));
        var picker = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
        await bot.SendCallbackAsync(Alice, picker.Buttons!.First(b => b.Text == "Cancel"));

        var games = bot.Services.GetRequiredService<XyzzyGameRepository>();
        Assert.Contains((await games.GetAsync(ChatOne, CancellationToken.None)).Players, p => p.PlayerId == Alice);
    }
}
