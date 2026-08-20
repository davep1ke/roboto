using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Persistence;
using Roboto.Bot.Xyzzy;

namespace Roboto.Bot.Tests.Xyzzy;

/// <summary>Covers CrCastPackImportService - live pack import/sync against a fake crcast HTTP
/// backend (no real network call). Mutates the shared CardCatalog static, so every test restores it
/// in a finally block, same pattern as CardCatalogOverrideTests/XyzzyPackFilteringTests.</summary>
public class CrCastPackImportServiceTests
{
    private const long ChatId = -900;

    /// <summary>Constructs its own throwaway TestBot/store rather than reusing the caller's - a
    /// try block's own `store` local isn't definitely assigned from the compiler's point of view if
    /// an exception happens before it's initialized, so the finally block can't safely reference it
    /// directly. Same pattern as XyzzyPackFilteringTests.cs.</summary>
    private static async Task RestoreCatalogAsync(IReadOnlyList<XyzzyCard> questions, IReadOnlyList<XyzzyCard> answers)
    {
        using var bot = new TestBot();
        var store = bot.Services.GetRequiredService<IStateStore>();
        await store.SaveAsync(CardCatalog.QuestionsKey, questions, CancellationToken.None);
        await store.SaveAsync(CardCatalog.AnswersKey, answers, CancellationToken.None);
        await CardCatalog.LoadOverrideAsync(store, CancellationToken.None);
        CardCatalog.ResetPacksForTesting();
    }

    [Fact]
    public async Task ImportingANewPackCodeAddsCardsAndThePack()
    {
        var originalQuestions = CardCatalog.Questions;
        var originalAnswers = CardCatalog.Answers;
        try
        {
            using var bot = new TestBot();
            var store = bot.Services.GetRequiredService<IStateStore>();

            var handler = new FakeCrCastHttpHandler();
            handler.ResponsesByExactUrl["https://api.crcast.cc/cc/decks/TESTPACK"] =
                """{"name":"Test Pack","description":"A pack for testing"}""";
            handler.ResponsesByExactUrl["https://api.crcast.cc/cc/decks/TESTPACK/cards"] =
                """{"calls":[{"text":["What's ","?"]}],"responses":[{"text":["An answer."]},{"text":["Another answer."]}]}""";
            var client = new CrCastClient(new HttpClient(handler));
            var service = new CrCastPackImportService(client, store, new XyzzyGameRepository(store));

            var outcome = await service.ImportOrSyncAsync("testpack", CancellationToken.None); // lowercase in, uppercase out

            Assert.True(outcome.Success);
            Assert.Contains("Importing fresh pack TESTPACK", outcome.Message);
            Assert.NotNull(outcome.PackId);

            var pack = CardCatalog.Packs.Single(p => p.Id == outcome.PackId);
            Assert.Equal("Test Pack", pack.Name);
            Assert.Equal("TESTPACK", pack.PackCode);
            Assert.NotNull(pack.NextSyncUtc);

            var question = CardCatalog.Questions.Single(q => q.PackId == pack.Id);
            Assert.Equal("What's __?", question.Text);
            Assert.Equal(1, question.AnswerCount);

            Assert.Equal(2, CardCatalog.Answers.Count(a => a.PackId == pack.Id));
        }
        finally
        {
            await RestoreCatalogAsync(originalQuestions, originalAnswers);
        }
    }

    [Fact]
    public async Task InvalidPackCodeIsRejectedWithoutAnyNetworkCall()
    {
        var originalQuestions = CardCatalog.Questions;
        var originalAnswers = CardCatalog.Answers;
        try
        {
            using var bot = new TestBot();
            var store = bot.Services.GetRequiredService<IStateStore>();
            var handler = new FakeCrCastHttpHandler(); // no responses configured - a real call would 404
            var client = new CrCastClient(new HttpClient(handler));
            var service = new CrCastPackImportService(client, store, new XyzzyGameRepository(store));

            var outcome = await service.ImportOrSyncAsync("not a valid code!", CancellationToken.None);

            Assert.False(outcome.Success);
            Assert.Null(outcome.PackId);
        }
        finally
        {
            await RestoreCatalogAsync(originalQuestions, originalAnswers);
        }
    }

    [Fact]
    public async Task FailedFetchReportsFailureAndChangesNothing()
    {
        var originalQuestions = CardCatalog.Questions;
        var originalAnswers = CardCatalog.Answers;
        try
        {
            using var bot = new TestBot();
            var store = bot.Services.GetRequiredService<IStateStore>();
            var handler = new FakeCrCastHttpHandler(); // 404s for everything
            var client = new CrCastClient(new HttpClient(handler));
            var service = new CrCastPackImportService(client, store, new XyzzyGameRepository(store));
            var packsBefore = CardCatalog.Packs.Count;

            var outcome = await service.ImportOrSyncAsync("MISSING", CancellationToken.None);

            Assert.False(outcome.Success);
            Assert.Contains("Failed to import pack", outcome.Message);
            Assert.Equal(packsBefore, CardCatalog.Packs.Count);
        }
        finally
        {
            await RestoreCatalogAsync(originalQuestions, originalAnswers);
        }
    }

    [Fact]
    public async Task ReSyncingAnExistingPackAddsChangesAndRemovesCards()
    {
        var originalQuestions = CardCatalog.Questions;
        var originalAnswers = CardCatalog.Answers;
        try
        {
            using var bot = new TestBot();
            var store = bot.Services.GetRequiredService<IStateStore>();

            // Seed an existing crcast-sourced pack with one question and two answers.
            var pack = new XyzzyPack("p1", "Old Name", PackCode: "SYNCPACK", NextSyncUtc: DateTime.UtcNow);
            var questions = new List<XyzzyCard> { new("q1", "Keeper question?", PackId: "p1") };
            var answers = new List<XyzzyCard> { new("a1", "Keeper answer.", PackId: "p1"), new("a2", "Doomed answer.", PackId: "p1") };
            await store.SaveAsync(CardCatalog.QuestionsKey, questions, CancellationToken.None);
            await store.SaveAsync(CardCatalog.AnswersKey, answers, CancellationToken.None);
            await store.SaveAsync(CardCatalog.PacksKey, new List<XyzzyPack> { pack }, CancellationToken.None);
            await CardCatalog.LoadOverrideAsync(store, CancellationToken.None);

            var handler = new FakeCrCastHttpHandler();
            handler.ResponsesByExactUrl["https://api.crcast.cc/cc/decks/SYNCPACK"] =
                """{"name":"New Name","description":"Updated"}""";
            // "Keeper question?"/"Keeper answer." survive unchanged; "Doomed answer." is gone;
            // "Fresh answer." is new.
            handler.ResponsesByExactUrl["https://api.crcast.cc/cc/decks/SYNCPACK/cards"] =
                """{"calls":[{"text":["Keeper question?"]}],"responses":[{"text":["Keeper answer."]},{"text":["Fresh answer."]}]}""";
            var client = new CrCastClient(new HttpClient(handler));
            var service = new CrCastPackImportService(client, store, new XyzzyGameRepository(store));

            var outcome = await service.ImportOrSyncAsync("syncpack", CancellationToken.None);

            Assert.True(outcome.Success);
            Assert.Contains("syncing cards", outcome.Message);

            var syncedPack = CardCatalog.Packs.Single(p => p.Id == "p1");
            Assert.Equal("New Name", syncedPack.Name);

            var packAnswers = CardCatalog.Answers.Where(a => a.PackId == "p1").ToList();
            Assert.Contains(packAnswers, a => a.Id == "a1" && a.Text == "Keeper answer."); // kept its ID
            Assert.Contains(packAnswers, a => a.Text == "Fresh answer.");
            Assert.DoesNotContain(packAnswers, a => a.Text == "Doomed answer.");
            Assert.DoesNotContain(CardCatalog.Answers, a => a.Id == "a2"); // the removed card's ID is gone entirely
        }
        finally
        {
            await RestoreCatalogAsync(originalQuestions, originalAnswers);
        }
    }

    [Fact]
    public async Task RemovedCardsInActiveGamesAreRemappedToASurvivorNotLeftDangling()
    {
        var originalQuestions = CardCatalog.Questions;
        var originalAnswers = CardCatalog.Answers;
        try
        {
            using var bot = new TestBot();
            var store = bot.Services.GetRequiredService<IStateStore>();

            var pack = new XyzzyPack("p1", "Sync Pack", PackCode: "REMAPPACK", NextSyncUtc: DateTime.UtcNow);
            var questions = new List<XyzzyCard> { new("q1", "A question?", PackId: "p1") };
            var answers = new List<XyzzyCard> { new("a1", "Survivor answer.", PackId: "p1"), new("a2", "Doomed answer.", PackId: "p1") };
            await store.SaveAsync(CardCatalog.QuestionsKey, questions, CancellationToken.None);
            await store.SaveAsync(CardCatalog.AnswersKey, answers, CancellationToken.None);
            await store.SaveAsync(CardCatalog.PacksKey, new List<XyzzyPack> { pack }, CancellationToken.None);
            await CardCatalog.LoadOverrideAsync(store, CancellationToken.None);

            var games = new XyzzyGameRepository(store);
            var game = await games.GetAsync(ChatId, CancellationToken.None);
            game.Status = XyzzyStatus.Question;
            game.Players.Add(new XyzzyPlayer { PlayerId = 1, DisplayName = "Alice", Hand = ["a2"] });
            game.RemainingAnswerCardIds = ["a2"];
            await games.SaveAsync(game, CancellationToken.None);

            var handler = new FakeCrCastHttpHandler();
            handler.ResponsesByExactUrl["https://api.crcast.cc/cc/decks/REMAPPACK"] = """{"name":"Sync Pack","description":""}""";
            // "Doomed answer." is gone from the fetched set - only "Survivor answer." remains.
            handler.ResponsesByExactUrl["https://api.crcast.cc/cc/decks/REMAPPACK/cards"] =
                """{"calls":[{"text":["A question?"]}],"responses":[{"text":["Survivor answer."]}]}""";
            var client = new CrCastClient(new HttpClient(handler));
            var service = new CrCastPackImportService(client, store, games);

            await service.ImportOrSyncAsync("REMAPPACK", CancellationToken.None);

            var after = await games.GetAsync(ChatId, CancellationToken.None);
            Assert.Equal(["a1"], after.Players[0].Hand); // remapped from the removed a2 onto the survivor a1
            Assert.Equal(["a1"], after.RemainingAnswerCardIds);
        }
        finally
        {
            await RestoreCatalogAsync(originalQuestions, originalAnswers);
        }
    }
}
