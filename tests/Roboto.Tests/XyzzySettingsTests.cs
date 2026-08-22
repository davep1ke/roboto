using System.Linq;
using RobotoChatBot;
using RobotoChatBot.Helpers;
using RobotoChatBot.Modules;

namespace RobotoTests;

/// <summary>
/// Deeper coverage of mod_xyzzy's /xyzzy_settings admin menu, beyond the happy-path game flow
/// already covered by XyzzyGameFlowTests: Kick, Change Score, Mess With, Extend, Reset, Re-deal,
/// Force Question, and pack filtering (All/None/individual toggle).
/// </summary>
public class XyzzySettingsTests
{
    private const long ChatId = -1000;
    private const long Alice = 100;
    private const long Bob = 101;
    private const long Carol = 102;

    private static void SeedCards(int questionCount = 5, int answerCount = 40)
    {
        var xyzzy = Plugins.plugins.OfType<mod_xyzzy>().Single();
        var coreData = (mod_xyzzy_coredata)xyzzy.getPluginData();
        coreData.questions.Clear();
        coreData.answers.Clear();
        for (int i = 0; i < questionCount; i++)
        {
            coreData.questions.Add(new mod_xyzzy_card($"Question {i} ___?", mod_xyzzy.primaryPackID, 1));
        }
        for (int i = 0; i < answerCount; i++)
        {
            coreData.answers.Add(new mod_xyzzy_card($"Answer {i}", mod_xyzzy.primaryPackID));
        }
    }

    private static TestHarness StartThreePlayerGame()
    {
        var bot = new TestHarness();
        SeedCards();
        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_start", "Alice");
        bot.TapButton(Alice, "Use Defaults", "Alice");
        bot.SendGroupMessage(ChatId, Bob, "/xyzzy_join", "Bob");
        bot.SendGroupMessage(ChatId, Carol, "/xyzzy_join", "Carol");
        bot.TapButton(Alice, "Start", "Alice");
        return bot;
    }

    private static mod_xyzzy_chatdata ChatData() => (mod_xyzzy_chatdata)Chats.getChat(ChatId).getPluginData(typeof(mod_xyzzy_chatdata), true);

    [Fact]
    public void KickRemovesThePlayerAndAnnouncesToTheGroup()
    {
        using var bot = StartThreePlayerGame();

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_settings", "Alice");
        bot.TapButton(Alice, "Kick", "Alice");
        string bobsButton = bot.LastKeyboardMessageTo(Alice).KeyboardRows!.SelectMany(r => r).Single(b => b.Text.Trim() == "Bob").Text;

        bot.TapButton(Alice, bobsButton, "Alice");

        var chatData = ChatData();
        Assert.DoesNotContain(chatData.players, p => p.name.Trim() == "Bob");
        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.StartsWith("Removed") && m.Text.Contains("Bob"));
    }

    [Fact]
    public void ChangeScoreSetsAPlayersScore()
    {
        using var bot = StartThreePlayerGame();

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_settings", "Alice");
        bot.TapButton(Alice, "Change Score", "Alice");
        string bobsButton = bot.LastKeyboardMessageTo(Alice).KeyboardRows!.SelectMany(r => r).Single(b => b.Text.Trim() == "Bob").Text;
        bot.TapButton(Alice, bobsButton, "Alice");
        Assert.Contains("new score", bot.BotClient.SentMessages[^1].Text);

        bot.TapButton(Alice, "7", "Alice");

        var chatData = ChatData();
        Assert.Equal(7, chatData.players.Single(p => p.name.Trim() == "Bob").wins);
    }

    [Fact]
    public void MessWithTogglesThePlayersCosmeticFlag()
    {
        using var bot = StartThreePlayerGame();

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_settings", "Alice");
        bot.TapButton(Alice, "Mess With", "Alice");
        string bobsButton = bot.LastKeyboardMessageTo(Alice).KeyboardRows!.SelectMany(r => r).Single(b => b.Text.Trim() == "Bob").Text;

        bot.TapButton(Alice, bobsButton, "Alice");

        var chatData = ChatData();
        Assert.True(chatData.players.Single(p => p.name.Trim() == "Bob").fuckedWith);
    }

    [Fact]
    public void ResetClearsPlayersAndStopsTheGame()
    {
        using var bot = StartThreePlayerGame();

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_settings", "Alice");
        bot.TapButton(Alice, "Reset", "Alice");

        var chatData = ChatData();
        Assert.Empty(chatData.players);
        Assert.Equal(xyzzy_Statuses.Stopped, chatData.status);
    }

    [Fact]
    public void ExtendResumesAStoppedGameWithTheSamePlayersAndScores()
    {
        using var bot = StartThreePlayerGame();
        var chatData = ChatData();
        chatData.players.Single(p => p.name.Trim() == "Bob").wins = 3;

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_settings", "Alice");
        bot.TapButton(Alice, "Abandon", "Alice");
        bot.TapButton(Alice, "Yes", "Alice");
        Assert.Equal(xyzzy_Statuses.Stopped, chatData.status);

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_settings", "Alice");
        bot.TapButton(Alice, "Extend", "Alice");

        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("Added additional cards"));
        Assert.Equal(xyzzy_Statuses.Question, chatData.status);
        Assert.Equal(3, chatData.players.Single(p => p.name.Trim() == "Bob").wins);
    }

    [Fact]
    public void ReDealDealsEveryoneAFreshHandButKeepsPlayersAndScores()
    {
        // reDeal clears everyone's hand and the remaining card pools, then immediately re-deals
        // (askQuestion(true)) rather than leaving the game stuck with empty hands - so "did it
        // work" is a fresh 10-card hand and a live Question round, not an empty one.
        using var bot = StartThreePlayerGame();
        var chatData = ChatData();
        chatData.players.Single(p => p.name.Trim() == "Bob").wins = 2;

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_settings", "Alice");
        bot.TapButton(Alice, "Re-deal", "Alice");

        Assert.Equal(3, chatData.players.Count);
        Assert.Equal(2, chatData.players.Single(p => p.name.Trim() == "Bob").wins);
        Assert.Equal(xyzzy_Statuses.Question, chatData.status);
        Assert.Equal(10, chatData.players.Single(p => p.name.Trim() == "Bob").cardsInHand.Count);
    }

    [Fact]
    public void ForceQuestionDealsANewRoundEvenIfPlayersHaventFinishedAnswering()
    {
        using var bot = StartThreePlayerGame();
        var chatData = ChatData();
        string firstQuestion = chatData.currentQuestion;

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_settings", "Alice");
        bot.TapButton(Alice, "Force Question", "Alice");

        Assert.Equal(xyzzy_Statuses.Question, chatData.status);
        Assert.NotEqual(firstQuestion, chatData.currentQuestion);
    }

    [Fact]
    public void ChangePacksNoneThenAllTogglesEveryPack()
    {
        using var bot = StartThreePlayerGame();
        var xyzzy = Plugins.plugins.OfType<mod_xyzzy>().Single();
        var coreData = (mod_xyzzy_coredata)xyzzy.getPluginData();
        coreData.packs.Add(new cardcast_pack("Extra Pack", "extra", "desc") { packID = System.Guid.NewGuid() });
        var chatData = ChatData();

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_settings", "Alice");
        bot.TapButton(Alice, "Change Packs", "Alice");
        bot.TapButton(Alice, "None", "Alice");
        Assert.Empty(chatData.packFilterIDs);

        bot.TapButton(Alice, "All", "Alice");
        Assert.Contains(mod_xyzzy.AllPacksEnabledID, chatData.packFilterIDs);
        Assert.True(chatData.packEnabled(coreData.packs[0].packID));
    }

    [Fact]
    public void TogglingAnIndividualPackFlipsItsFilterState()
    {
        using var bot = StartThreePlayerGame();
        var xyzzy = Plugins.plugins.OfType<mod_xyzzy>().Single();
        var coreData = (mod_xyzzy_coredata)xyzzy.getPluginData();
        var extraPack = new cardcast_pack("Extra Pack", "extra", "desc");
        coreData.packs.Add(extraPack);
        var chatData = ChatData();
        Assert.False(chatData.packEnabled(extraPack.packID));

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_settings", "Alice");
        bot.TapButton(Alice, "Change Packs", "Alice");
        string packButton = bot.LastKeyboardMessageTo(Alice).KeyboardRows!.SelectMany(r => r).Single(b => b.Text.Contains("Extra Pack")).Text;

        bot.TapButton(Alice, packButton, "Alice");

        Assert.True(chatData.packEnabled(extraPack.packID));
    }
}
