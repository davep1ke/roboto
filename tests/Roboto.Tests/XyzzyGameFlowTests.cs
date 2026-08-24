using System.Linq;
using RobotoChatBot;
using RobotoChatBot.Modules;

namespace RobotoTests;

/// <summary>
/// Drives the primary payload (mod_xyzzy) through a full round: start, join, deal, answer, judge.
/// Legacy's real card catalog only ever gets populated via CardCast/CrCast import (a network call),
/// so these tests seed mod_xyzzy_coredata.questions/answers directly with synthetic cards under
/// mod_xyzzy.dummyPackID (an arbitrary marker GUID here, not a pack these tests ever register) -
/// rather than exercising the importer, that's a separate concern. Drawable by default because no
/// "CAHBS"-coded pack exists in test data, so chatdata.packFilterIDs's lazily-resolved default
/// falls through to mod_xyzzy.AllPacksEnabledID (see mod_xyzzy_chatdata.ensureDefaultPackFilter) -
/// TestHarness also clears the coredata's auto-seeded "ZZ Dummy Pack" so it can't interfere.
/// </summary>
public class XyzzyGameFlowTests
{
    private const long ChatId = -200;
    private const long Alice = 10;
    private const long Bob = 11;
    private const long Carol = 12;

    private static void SeedCards(int questionCount = 5, int answerCount = 40)
    {
        var xyzzy = Plugins.plugins.OfType<mod_xyzzy>().Single();
        var coreData = (mod_xyzzy_coredata)xyzzy.getPluginData();
        coreData.questions.Clear();
        coreData.answers.Clear();
        for (int i = 0; i < questionCount; i++)
        {
            coreData.questions.Add(new mod_xyzzy_card($"Question {i} ___?", mod_xyzzy.dummyPackID, 1));
        }
        for (int i = 0; i < answerCount; i++)
        {
            coreData.answers.Add(new mod_xyzzy_card($"Answer {i}", mod_xyzzy.dummyPackID));
        }
    }

    [Fact]
    public void FullRoundAwardsAPointAndStartsTheNextRound()
    {
        using var bot = new TestHarness();
        SeedCards();

        // Alice starts the game - goes to a DM asking Use Defaults / Configure Game / Cancel.
        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_start", "Alice");
        bot.TapButton(Alice, "Use Defaults", "Alice");
        bot.TapButton(Alice, "Add Bots", "Alice"); // Use Defaults now auto-adds 2 bots - clear them for a clean human-only baseline
        bot.TapButton(Alice, "Remove All Bots", "Alice");

        // Bob and Carol join from the group.
        bot.SendGroupMessage(ChatId, Bob, "/xyzzy_join", "Bob");
        bot.SendGroupMessage(ChatId, Carol, "/xyzzy_join", "Carol");

        // Alice (the starter) is players[0], so she's tzar for round 1 - starting the game with 3
        // players deals Bob and Carol a hand each and asks them to answer.
        bot.TapButton(Alice, "Start", "Alice");

        var chatData = (mod_xyzzy_chatdata)Chats.getChat(ChatId).getPluginData(typeof(mod_xyzzy_chatdata), true);
        Assert.Equal(xyzzy_Statuses.Question, chatData.status);

        string bobsAnswer = bot.LastKeyboardMessageTo(Bob).KeyboardRows![0][0].Text;
        string carolsAnswer = bot.LastKeyboardMessageTo(Carol).KeyboardRows![0][0].Text;

        bot.TapButton(Bob, bobsAnswer, "Bob");
        bot.TapButton(Carol, carolsAnswer, "Carol");

        // Both non-judge players have answered, so judging should have started automatically -
        // Alice (the tzar) gets a keyboard of the submitted answers to pick the winner from.
        Assert.Equal(xyzzy_Statuses.Judging, chatData.status);
        var judgingKeyboard = bot.LastKeyboardMessageTo(Alice).KeyboardRows!;
        string winningAnswer = judgingKeyboard[0][0].Text;
        Assert.Contains(new[] { bobsAnswer, carolsAnswer }, a => a == winningAnswer);

        bot.TapButton(Alice, winningAnswer, "Alice");

        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("wins a point!"));

        var winner = chatData.players.Single(p => p.wins == 1);
        Assert.Contains(winner.name.Trim(), new[] { "Bob", "Carol" });
        Assert.Equal(0, chatData.players.Single(p => p.playerID != winner.playerID && p.name.Trim() != "Alice").wins);
    }

    [Fact]
    public void CannotStartWithFewerThanThreePlayers()
    {
        using var bot = new TestHarness();
        SeedCards();

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_start", "Alice");
        bot.TapButton(Alice, "Use Defaults", "Alice");
        bot.TapButton(Alice, "Add Bots", "Alice"); // Use Defaults now auto-adds 2 bots - clear them for a clean human-only baseline
        bot.TapButton(Alice, "Remove All Bots", "Alice");
        bot.SendGroupMessage(ChatId, Bob, "/xyzzy_join", "Bob");

        bot.TapButton(Alice, "Start", "Alice");

        var chatData = (mod_xyzzy_chatdata)Chats.getChat(ChatId).getPluginData(typeof(mod_xyzzy_chatdata), true);
        Assert.Equal(xyzzy_Statuses.Invites, chatData.status);
        Assert.Contains("Not enough players", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public void MultiAnswerQuestionRequiresTwoCardsPerPlayerAndCombinesThemForJudging()
    {
        // "Pick 2" questions (mod_xyzzy_card.nrAnswers > 1) are legacy-native (confirmed present in
        // legacy-winforms-baseline verbatim - see MIGRATION.md phase 9's notes on why this delta was
        // already-true-by-construction here), but hadn't had a dedicated round-loop test until now -
        // FullRoundAwardsAPointAndStartsTheNextRound only exercises the single-answer case.
        using var bot = new TestHarness();
        var xyzzy = Plugins.plugins.OfType<mod_xyzzy>().Single();
        var coreData = (mod_xyzzy_coredata)xyzzy.getPluginData();
        coreData.questions.Clear();
        coreData.answers.Clear();
        coreData.questions.Add(new mod_xyzzy_card("First: ___. Second: ___.", mod_xyzzy.dummyPackID, 2));
        for (int i = 0; i < 40; i++) { coreData.answers.Add(new mod_xyzzy_card("Answer " + i, mod_xyzzy.dummyPackID)); }

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_start", "Alice");
        bot.TapButton(Alice, "Use Defaults", "Alice");
        bot.TapButton(Alice, "Add Bots", "Alice"); // Use Defaults now auto-adds 2 bots - clear them for a clean human-only baseline
        bot.TapButton(Alice, "Remove All Bots", "Alice");
        bot.SendGroupMessage(ChatId, Bob, "/xyzzy_join", "Bob");
        bot.SendGroupMessage(ChatId, Carol, "/xyzzy_join", "Carol");
        bot.TapButton(Alice, "Start", "Alice");

        var chatData = (mod_xyzzy_chatdata)Chats.getChat(ChatId).getPluginData(typeof(mod_xyzzy_chatdata), true);
        Assert.Equal(xyzzy_Statuses.Question, chatData.status);

        string bobsFirst = bot.LastKeyboardMessageTo(Bob).KeyboardRows![0][0].Text;
        bot.TapButton(Bob, bobsFirst, "Bob");
        Assert.Contains("Pick your next card", bot.BotClient.SentMessages[^1].Text);
        string bobsSecond = bot.LastKeyboardMessageTo(Bob).KeyboardRows![0][0].Text;
        bot.TapButton(Bob, bobsSecond, "Bob");

        string carolsFirst = bot.LastKeyboardMessageTo(Carol).KeyboardRows![0][0].Text;
        bot.TapButton(Carol, carolsFirst, "Carol");
        string carolsSecond = bot.LastKeyboardMessageTo(Carol).KeyboardRows![0][0].Text;
        bot.TapButton(Carol, carolsSecond, "Carol");

        // Both players have submitted their full 2-card answer, so judging starts automatically -
        // the judging keyboard shows each player's two cards joined with " >> ".
        Assert.Equal(xyzzy_Statuses.Judging, chatData.status);
        string bobsCombined = bobsFirst + " >> " + bobsSecond;
        string carolsCombined = carolsFirst + " >> " + carolsSecond;
        var judgingKeyboard = bot.LastKeyboardMessageTo(Alice).KeyboardRows!;
        string winningAnswer = judgingKeyboard[0][0].Text;
        Assert.Contains(new[] { bobsCombined, carolsCombined }, a => a == winningAnswer);

        bot.TapButton(Alice, winningAnswer, "Alice");

        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("wins a point!"));
        var winner = chatData.players.Single(p => p.wins == 1);
        Assert.Contains(winner.name.Trim(), new[] { "Bob", "Carol" });
    }

    [Fact]
    public void WinMessageAddsSpacesAroundABlankGluedToSurroundingWordsInImportedPackData()
    {
        // Imported pack data is inconsistent about whether a blank has spaces around it - this
        // question text (glued "found_when", no spaces) is the shape a real live-tested pack
        // actually produced. judgesResponse's substitution used to paste the answer straight in
        // with no added spacing, gluing it to both neighbouring words.
        using var bot = new TestHarness();
        var xyzzy = Plugins.plugins.OfType<mod_xyzzy>().Single();
        var coreData = (mod_xyzzy_coredata)xyzzy.getPluginData();
        coreData.questions.Clear();
        coreData.answers.Clear();
        coreData.questions.Add(new mod_xyzzy_card("I found_when I was cleaning", mod_xyzzy.dummyPackID, 1));
        coreData.answers.Add(new mod_xyzzy_card("a spider", mod_xyzzy.dummyPackID));

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_start", "Alice");
        bot.TapButton(Alice, "Use Defaults", "Alice");
        bot.TapButton(Alice, "Add Bots", "Alice"); // Use Defaults now auto-adds 2 bots - clear them for a clean human-only baseline
        bot.TapButton(Alice, "Remove All Bots", "Alice");
        bot.SendGroupMessage(ChatId, Bob, "/xyzzy_join", "Bob");
        // Override (legacy's own existing escape hatch) rather than joining a third player - only
        // one non-judge player needed to keep this deterministic.
        bot.TapButton(Alice, "Override", "Alice");

        string bobsAnswer = bot.LastKeyboardMessageTo(Bob).KeyboardRows![0][0].Text;
        bot.TapButton(Bob, bobsAnswer, "Bob");
        string winningAnswer = bot.LastKeyboardMessageTo(Alice).KeyboardRows![0][0].Text;
        bot.TapButton(Alice, winningAnswer, "Alice");

        var winMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == ChatId && m.Text.Contains("wins a point!"));
        Assert.Contains("I found *a spider* when I was cleaning", winMessage.Text);
    }

    [Fact]
    public void WinMessageDoesNotDoubleSpaceABlankThatsAlreadyProperlyFormatted()
    {
        using var bot = new TestHarness();
        var xyzzy = Plugins.plugins.OfType<mod_xyzzy>().Single();
        var coreData = (mod_xyzzy_coredata)xyzzy.getPluginData();
        coreData.questions.Clear();
        coreData.answers.Clear();
        coreData.questions.Add(new mod_xyzzy_card("I found ___ when I was cleaning", mod_xyzzy.dummyPackID, 1));
        coreData.answers.Add(new mod_xyzzy_card("a spider", mod_xyzzy.dummyPackID));

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_start", "Alice");
        bot.TapButton(Alice, "Use Defaults", "Alice");
        bot.TapButton(Alice, "Add Bots", "Alice"); // Use Defaults now auto-adds 2 bots - clear them for a clean human-only baseline
        bot.TapButton(Alice, "Remove All Bots", "Alice");
        bot.SendGroupMessage(ChatId, Bob, "/xyzzy_join", "Bob");
        bot.TapButton(Alice, "Override", "Alice");

        string bobsAnswer = bot.LastKeyboardMessageTo(Bob).KeyboardRows![0][0].Text;
        bot.TapButton(Bob, bobsAnswer, "Bob");
        string winningAnswer = bot.LastKeyboardMessageTo(Alice).KeyboardRows![0][0].Text;
        bot.TapButton(Alice, winningAnswer, "Alice");

        var winMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == ChatId && m.Text.Contains("wins a point!"));
        // Exact-match on the substituted sentence, not a blanket "no double space anywhere in the
        // message" check - the winner name line has its own unrelated pre-existing quirk (a
        // trailing space from userFullName's firstName + " " + emptySurname construction collides
        // with the literal " wins a point!" that follows it, e.g. "Bob  wins a point!") that has
        // nothing to do with this substitution logic.
        Assert.Contains("I found *a spider* when I was cleaning", winMessage.Text);
        Assert.DoesNotContain("*a spider*  when", winMessage.Text);
        Assert.DoesNotContain("found  *a spider*", winMessage.Text);
    }

    [Fact]
    public void WinMessageDoesNotInsertASpaceBeforeTrailingPunctuation()
    {
        using var bot = new TestHarness();
        var xyzzy = Plugins.plugins.OfType<mod_xyzzy>().Single();
        var coreData = (mod_xyzzy_coredata)xyzzy.getPluginData();
        coreData.questions.Clear();
        coreData.answers.Clear();
        coreData.questions.Add(new mod_xyzzy_card("I like to___.", mod_xyzzy.dummyPackID, 1));
        coreData.answers.Add(new mod_xyzzy_card("nap", mod_xyzzy.dummyPackID));

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_start", "Alice");
        bot.TapButton(Alice, "Use Defaults", "Alice");
        bot.TapButton(Alice, "Add Bots", "Alice"); // Use Defaults now auto-adds 2 bots - clear them for a clean human-only baseline
        bot.TapButton(Alice, "Remove All Bots", "Alice");
        bot.SendGroupMessage(ChatId, Bob, "/xyzzy_join", "Bob");
        bot.TapButton(Alice, "Override", "Alice");

        string bobsAnswer = bot.LastKeyboardMessageTo(Bob).KeyboardRows![0][0].Text;
        bot.TapButton(Bob, bobsAnswer, "Bob");
        string winningAnswer = bot.LastKeyboardMessageTo(Alice).KeyboardRows![0][0].Text;
        bot.TapButton(Alice, winningAnswer, "Alice");

        var winMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == ChatId && m.Text.Contains("wins a point!"));
        Assert.Contains("I like to *nap*.", winMessage.Text);
    }

    [Fact]
    public void JoiningTwiceIsIdempotent()
    {
        using var bot = new TestHarness();
        SeedCards();

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_start", "Alice");
        bot.TapButton(Alice, "Use Defaults", "Alice");
        bot.TapButton(Alice, "Add Bots", "Alice"); // Use Defaults now auto-adds 2 bots - clear them for a clean human-only baseline
        bot.TapButton(Alice, "Remove All Bots", "Alice");

        bot.SendGroupMessage(ChatId, Bob, "/xyzzy_join", "Bob");
        bot.SendGroupMessage(ChatId, Bob, "/xyzzy_join", "Bob");

        var chatData = (mod_xyzzy_chatdata)Chats.getChat(ChatId).getPluginData(typeof(mod_xyzzy_chatdata), true);
        Assert.Equal(2, chatData.players.Count); // Alice (starter) + Bob, not double-counted
        Assert.Contains("already in the game", bot.BotClient.SentMessages[^1].Text);
    }
}
