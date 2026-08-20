using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Persistence;
using Roboto.Bot.Xyzzy;

namespace Roboto.Bot.Tests.Xyzzy;

/// <summary>Covers a real bug hit live: a crcast-imported question whose blank is a run of several
/// underscores ("...you immediately __.", not a single "_") came back with the winning answer
/// duplicated back-to-back and no separator ("...immediately Thrall's ballsThrall's balls.").
/// XyzzyRoundService.PickWinnerAsync used to substitute with a plain string Replace("_", answer),
/// which swaps in the answer once per individual underscore character rather than once per blank -
/// fixed via Regex.Replace("_+", ...) so a whole run of underscores is treated as one blank. Uses a
/// catalog override (CardCatalog is a shared static across the whole test run - restored in a
/// finally block, same pattern as CardCatalogOverrideTests/XyzzyPackFilteringTests) since none of
/// the hardcoded placeholder questions have a multi-underscore blank.</summary>
public class XyzzyWinAnnouncementFormattingTests
{
    private const long ChatId = -930;
    private const long Alice = 1;

    [Fact]
    public async Task AMultiUnderscoreBlankIsSubstitutedOnceNotOncePerUnderscoreCharacter()
    {
        using var bot = new TestBot();
        var store = bot.Services.GetRequiredService<IStateStore>();
        var games = bot.Services.GetRequiredService<XyzzyGameRepository>();

        var originalQuestions = CardCatalog.Questions;
        var originalAnswers = CardCatalog.Answers;
        try
        {
            var questions = new List<XyzzyCard>
            {
                new("q1", "When you gain the ability to do anything, you immediately __."),
            };
            // 3 players (Alice + 2 auto-filled bots) each need a full 10-card hand for round 1.
            var answers = Enumerable.Range(1, 35).Select(i => new XyzzyCard($"a{i}", $"Answer {i}")).ToList();
            await store.SaveAsync(CardCatalog.QuestionsKey, questions, CancellationToken.None);
            await store.SaveAsync(CardCatalog.AnswersKey, answers, CancellationToken.None);
            await CardCatalog.LoadOverrideAsync(store, CancellationToken.None);

            await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_start", firstName: "Alice"));
            var choiceMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
            await bot.SendCallbackAsync(Alice, choiceMessage.Buttons!.First(b => b.Text == "Use Defaults"));

            // QuestionLimit = 1 so the game ends after round 1 rather than drawing a second round -
            // there's only one question card in this override, and the only-question-in-the-catalog
            // gets excluded from its own redraw pool once it's already been asked.
            var game = await games.GetAsync(ChatId, CancellationToken.None);
            game.QuestionLimit = 1;
            await games.SaveAsync(game, CancellationToken.None);

            var startMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 } && m.Buttons.Any(b => b.Text == "Start"));
            await bot.SendCallbackAsync(Alice, startMessage.Buttons!.First(b => b.Text == "Start"));

            // Alice is round 1's judge (Players[0], the solo starter) - both bots auto-answered.
            var judgeMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Text.Contains("Pick the best answer"));
            var winningButton = judgeMessage.Buttons![0];
            var winningAnswer = winningButton.Text;
            await bot.SendCallbackAsync(Alice, winningButton);

            var winMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == ChatId && m.Text.Contains("wins a point"));
            Assert.Contains($"immediately *{winningAnswer}*.", winMessage.Text);
            Assert.DoesNotContain($"{winningAnswer}{winningAnswer}", winMessage.Text);
        }
        finally
        {
            await store.SaveAsync(CardCatalog.QuestionsKey, originalQuestions, CancellationToken.None);
            await store.SaveAsync(CardCatalog.AnswersKey, originalAnswers, CancellationToken.None);
            await CardCatalog.LoadOverrideAsync(store, CancellationToken.None);
        }
    }
}
