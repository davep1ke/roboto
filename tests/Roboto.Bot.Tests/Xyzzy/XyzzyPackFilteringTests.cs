using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Persistence;
using Roboto.Bot.Xyzzy;

namespace Roboto.Bot.Tests.Xyzzy;

/// <summary>Covers pack filtering (XyzzyGameState.EnabledPackIds) - deck draws respecting a chat's
/// selected packs, and the /xyzzy_settings "Change Packs" toggle UI. Mutates the shared CardCatalog
/// static via LoadOverrideAsync, so every test restores it in a finally block (same pattern as
/// CardCatalogOverrideTests) to avoid leaking pack-tagged test data into unrelated tests.</summary>
public class XyzzyPackFilteringTests
{
    private const long ChatId = -700;
    private const long Alice = 1;
    private const long Bob = 2;
    private const long Carol = 3;

    private static async Task SeedTwoPackCatalogAsync(IStateStore store)
    {
        var questions = new List<XyzzyCard>
        {
            new("pq1", "Pack one question?", PackId: "p1"),
            new("pq2", "Pack two question?", PackId: "p2"),
        };
        var answers = new List<XyzzyCard>
        {
            new("pa01", "Pack one answer A.", PackId: "p1"),
            new("pa02", "Pack one answer B.", PackId: "p1"),
            new("pa03", "Pack one answer C.", PackId: "p1"),
            new("pa04", "Pack one answer D.", PackId: "p1"),
            new("pa05", "Pack one answer E.", PackId: "p1"),
            new("pa06", "Pack one answer F.", PackId: "p1"),
            new("pa07", "Pack one answer G.", PackId: "p1"),
            new("pa08", "Pack one answer H.", PackId: "p1"),
            new("pa09", "Pack one answer I.", PackId: "p1"),
            new("pa10", "Pack one answer J.", PackId: "p1"),
            new("pa11", "Pack one answer K.", PackId: "p1"),
            new("pa12", "Pack one answer L.", PackId: "p1"),
            new("pa13", "Pack one answer M.", PackId: "p1"),
            new("pa14", "Pack one answer N.", PackId: "p1"),
            new("pa15", "Pack one answer O.", PackId: "p1"),
            new("pa16", "Pack one answer P.", PackId: "p1"),
            new("pa17", "Pack one answer Q.", PackId: "p1"),
            new("pa18", "Pack one answer R.", PackId: "p1"),
            new("pa19", "Pack one answer S.", PackId: "p1"),
            new("pa20", "Pack one answer T.", PackId: "p1"),
            new("pa21", "Pack one answer U.", PackId: "p1"),
            new("pa22", "Pack one answer V.", PackId: "p1"),
            new("pa23", "Pack one answer W.", PackId: "p1"),
            new("pa24", "Pack one answer X.", PackId: "p1"),
            new("pa25", "Pack one answer Y.", PackId: "p1"),
            new("pa26", "Pack one answer Z.", PackId: "p1"),
            new("pa27", "Pack one answer AA.", PackId: "p1"),
            new("pa28", "Pack one answer BB.", PackId: "p1"),
            new("pa29", "Pack one answer CC.", PackId: "p1"),
            new("pa30", "Pack one answer DD.", PackId: "p1"),
            new("pb01", "Pack two answer A.", PackId: "p2"),
            new("pb02", "Pack two answer B.", PackId: "p2"),
            new("pb03", "Pack two answer C.", PackId: "p2"),
            new("pb04", "Pack two answer D.", PackId: "p2"),
            new("pb05", "Pack two answer E.", PackId: "p2"),
            new("pb06", "Pack two answer F.", PackId: "p2"),
            new("pb07", "Pack two answer G.", PackId: "p2"),
            new("pb08", "Pack two answer H.", PackId: "p2"),
            new("pb09", "Pack two answer I.", PackId: "p2"),
            new("pb10", "Pack two answer J.", PackId: "p2"),
        };
        var packs = new List<XyzzyPack> { new("p1", "Pack One"), new("p2", "Pack Two") };
        await store.SaveAsync(CardCatalog.QuestionsKey, questions, CancellationToken.None);
        await store.SaveAsync(CardCatalog.AnswersKey, answers, CancellationToken.None);
        await store.SaveAsync(CardCatalog.PacksKey, packs, CancellationToken.None);
        await CardCatalog.LoadOverrideAsync(store, CancellationToken.None);
    }

    private static async Task RestoreCatalogAsync(IReadOnlyList<XyzzyCard> questions, IReadOnlyList<XyzzyCard> answers)
    {
        using var bot = new TestBot();
        var store = bot.Services.GetRequiredService<IStateStore>();
        await store.SaveAsync(CardCatalog.QuestionsKey, questions, CancellationToken.None);
        await store.SaveAsync(CardCatalog.AnswersKey, answers, CancellationToken.None);
        await CardCatalog.LoadOverrideAsync(store, CancellationToken.None);
        CardCatalog.ResetPacksForTesting();
    }

    private static async Task<XyzzyGameState> StartThreePlayerGameAsync(TestBot bot)
    {
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_start", firstName: "Alice"));
        var choiceMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
        await bot.SendCallbackAsync(Alice, choiceMessage.Buttons!.First(b => b.Text == "Use Defaults"));
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Bob, "/xyzzy_join", firstName: "Bob"));
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Carol, "/xyzzy_join", firstName: "Carol"));
        var startMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 } && m.Buttons.Any(b => b.Text == "Start"));
        await bot.SendCallbackAsync(Alice, startMessage.Buttons!.First(b => b.Text == "Start"));

        return await bot.Services.GetRequiredService<XyzzyGameRepository>().GetAsync(ChatId, CancellationToken.None);
    }

    [Fact]
    public async Task RestrictingToOnePackOnlyDealsFromThatPack()
    {
        var originalQuestions = CardCatalog.Questions;
        var originalAnswers = CardCatalog.Answers;
        try
        {
            using var bot = new TestBot();
            var store = bot.Services.GetRequiredService<IStateStore>();
            await SeedTwoPackCatalogAsync(store);

            var games = bot.Services.GetRequiredService<XyzzyGameRepository>();
            await games.SaveAsync(new XyzzyGameState { ChatId = ChatId, EnabledPackIds = ["p1"] }, CancellationToken.None);

            var game = await StartThreePlayerGameAsync(bot);

            Assert.Equal("pq1", game.CurrentQuestionCardId);
            Assert.All(game.Players, p => Assert.All(p.Hand, cardId => Assert.StartsWith("pa", cardId)));
        }
        finally
        {
            await RestoreCatalogAsync(originalQuestions, originalAnswers);
        }
    }

    [Fact]
    public async Task AnEmptyEnabledPackListDealsFromEveryPack()
    {
        var originalQuestions = CardCatalog.Questions;
        var originalAnswers = CardCatalog.Answers;
        try
        {
            using var bot = new TestBot();
            var store = bot.Services.GetRequiredService<IStateStore>();
            await SeedTwoPackCatalogAsync(store);

            var game = await StartThreePlayerGameAsync(bot);

            Assert.Contains(game.CurrentQuestionCardId, new[] { "pq1", "pq2" });
        }
        finally
        {
            await RestoreCatalogAsync(originalQuestions, originalAnswers);
        }
    }

    [Fact]
    public async Task AStaleFilterMatchingNoPacksFallsBackToTheFullCatalog()
    {
        // Guards against a chat's EnabledPackIds surviving a later import that dropped/renamed the
        // packs it referenced - the filter should never be able to leave a deck empty and hang.
        var originalQuestions = CardCatalog.Questions;
        var originalAnswers = CardCatalog.Answers;
        try
        {
            using var bot = new TestBot();
            var store = bot.Services.GetRequiredService<IStateStore>();
            await SeedTwoPackCatalogAsync(store);

            var games = bot.Services.GetRequiredService<XyzzyGameRepository>();
            await games.SaveAsync(new XyzzyGameState { ChatId = ChatId, EnabledPackIds = ["does-not-exist"] }, CancellationToken.None);

            var game = await StartThreePlayerGameAsync(bot);

            Assert.Contains(game.CurrentQuestionCardId, new[] { "pq1", "pq2" });
            Assert.All(game.Players, p => Assert.NotEmpty(p.Hand));
        }
        finally
        {
            await RestoreCatalogAsync(originalQuestions, originalAnswers);
        }
    }

    [Fact]
    public async Task ChangePacksTogglesAPackOffThenBackOn()
    {
        var originalQuestions = CardCatalog.Questions;
        var originalAnswers = CardCatalog.Answers;
        try
        {
            using var bot = new TestBot();
            var store = bot.Services.GetRequiredService<IStateStore>();
            await SeedTwoPackCatalogAsync(store);

            await StartThreePlayerGameAsync(bot); // Alice judges round 1, clearing her DM queue

            await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_settings"));
            var menuMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
            await bot.SendCallbackAsync(Alice, menuMessage.Buttons!.First(b => b.Text == "Change Packs"));

            var picker = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
            // Nothing's been narrowed yet (EnabledPackIds starts empty, meaning "all packs") - both
            // show pre-checked.
            Assert.Contains(picker.Buttons!, b => b.Text == "✓ Pack One");
            Assert.Contains(picker.Buttons!, b => b.Text == "✓ Pack Two");

            await bot.SendCallbackAsync(Alice, picker.Buttons!.First(b => b.Text == "✓ Pack One"));
            var afterToggleOff = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
            Assert.Contains(afterToggleOff.Buttons!, b => b.Text == "Pack One");
            Assert.DoesNotContain(afterToggleOff.Buttons!, b => b.Text == "✓ Pack One");
            Assert.Contains(afterToggleOff.Buttons!, b => b.Text == "✓ Pack Two");

            var games = bot.Services.GetRequiredService<XyzzyGameRepository>();
            var afterOff = await games.GetAsync(ChatId, CancellationToken.None);
            Assert.DoesNotContain("p1", afterOff.EnabledPackIds);
            Assert.Contains("p2", afterOff.EnabledPackIds);

            await bot.SendCallbackAsync(Alice, afterToggleOff.Buttons!.First(b => b.Text == "Enable All Packs"));
            var afterEnableAll = await games.GetAsync(ChatId, CancellationToken.None);
            Assert.Empty(afterEnableAll.EnabledPackIds);
        }
        finally
        {
            await RestoreCatalogAsync(originalQuestions, originalAnswers);
        }
    }
}
