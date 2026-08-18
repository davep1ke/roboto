using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Persistence;
using Roboto.Bot.Xyzzy;

namespace Roboto.Bot.Tests.Xyzzy;

/// <summary>Covers CardCatalog.LoadOverrideAsync - phase 11's mechanism for swapping in a real,
/// imported catalog per instance without every XyzzyRoundService call site needing to load it
/// asynchronously. Runs last (or at least isolated) within the process since CardCatalog is a
/// shared static - see the cleanup pattern each test uses to restore the hardcoded defaults
/// afterward so it can't leak into any other test in the same run.</summary>
public class CardCatalogOverrideTests
{
    [Fact]
    public async Task NoStoredCatalogLeavesTheHardcodedDefaultsInPlace()
    {
        using var bot = new TestBot();
        var store = bot.Services.GetRequiredService<IStateStore>();

        var before = CardCatalog.Questions;
        await CardCatalog.LoadOverrideAsync(store, CancellationToken.None);

        Assert.Same(before, CardCatalog.Questions);
    }

    [Fact]
    public async Task AStoredCatalogReplacesTheDefaultsEntirely()
    {
        using var bot = new TestBot();
        var store = bot.Services.GetRequiredService<IStateStore>();

        var originalQuestions = CardCatalog.Questions;
        var originalAnswers = CardCatalog.Answers;
        try
        {
            var importedQuestions = new List<XyzzyCard> { new("iq1", "Imported question?") };
            var importedAnswers = new List<XyzzyCard> { new("ia1", "Imported answer.") };
            await store.SaveAsync(CardCatalog.QuestionsKey, importedQuestions, CancellationToken.None);
            await store.SaveAsync(CardCatalog.AnswersKey, importedAnswers, CancellationToken.None);

            await CardCatalog.LoadOverrideAsync(store, CancellationToken.None);

            Assert.Single(CardCatalog.Questions);
            Assert.Equal("iq1", CardCatalog.Questions[0].Id);
            Assert.Single(CardCatalog.Answers);
            Assert.Equal("ia1", CardCatalog.Answers[0].Id);
        }
        finally
        {
            // CardCatalog is a shared static - restore the defaults so this doesn't leak into
            // whichever test happens to run next in the same process.
            await RestoreDefaultsAsync(originalQuestions, originalAnswers);
        }
    }

    private static async Task RestoreDefaultsAsync(IReadOnlyList<XyzzyCard> questions, IReadOnlyList<XyzzyCard> answers)
    {
        using var bot = new TestBot();
        var store = bot.Services.GetRequiredService<IStateStore>();
        await store.SaveAsync(CardCatalog.QuestionsKey, questions, CancellationToken.None);
        await store.SaveAsync(CardCatalog.AnswersKey, answers, CancellationToken.None);
        await CardCatalog.LoadOverrideAsync(store, CancellationToken.None);
    }
}
