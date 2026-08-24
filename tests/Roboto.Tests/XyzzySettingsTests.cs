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
            coreData.questions.Add(new mod_xyzzy_card($"Question {i} ___?", mod_xyzzy.dummyPackID, 1));
        }
        for (int i = 0; i < answerCount; i++)
        {
            coreData.answers.Add(new mod_xyzzy_card($"Answer {i}", mod_xyzzy.dummyPackID));
        }
    }

    private static TestHarness StartThreePlayerGame()
    {
        var bot = new TestHarness();
        SeedCards();
        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_start", "Alice");
        bot.TapButton(Alice, "Use Defaults", "Alice");
        bot.TapButton(Alice, "Add Bots", "Alice"); // Use Defaults now auto-adds 2 bots - clear them for a clean human-only baseline
        bot.TapButton(Alice, "Remove All Bots", "Alice");
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
        // No "CAHBS"-coded pack exists in test data, so the default pack filter resolves to "every
        // pack enabled" (mod_xyzzy.AllPacksEnabledID) - including this one, added after that default
        // was already resolved by StartThreePlayerGame() dealing the first round. Start from "None"
        // (every pack explicitly disabled) so toggling one pack back on is what's actually verified.
        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_settings", "Alice");
        bot.TapButton(Alice, "Change Packs", "Alice");
        bot.TapButton(Alice, "None", "Alice");
        Assert.False(chatData.packEnabled(extraPack.packID));

        string packButton = bot.LastKeyboardMessageTo(Alice).KeyboardRows!.SelectMany(r => r).Single(b => b.Text.Contains("Extra Pack")).Text;

        bot.TapButton(Alice, packButton, "Alice");

        Assert.True(chatData.packEnabled(extraPack.packID));
    }

    [Fact]
    public void CardCastImportCancelReturnsToThePackFilterScreenWithoutImportingAnything()
    {
        using var bot = StartThreePlayerGame();
        var xyzzy = Plugins.plugins.OfType<mod_xyzzy>().Single();
        var coreData = (mod_xyzzy_coredata)xyzzy.getPluginData();
        int packCountBefore = coreData.packs.Count;

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_settings", "Alice");
        bot.TapButton(Alice, "Change Packs", "Alice");
        bot.TapButton(Alice, "Import Pack", "Alice");
        Assert.Contains("enter the pack code", bot.BotClient.SentMessages[^1].Text);

        bot.TapButton(Alice, "Cancel", "Alice");

        Assert.Equal(packCountBefore, coreData.packs.Count);
        Assert.Contains("following packs", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public void CardCastImportSuccessfullyAddsANewPackAndEnablesItsFilter()
    {
        // cardCast.HttpGetOverride (mirroring TelegramAPI.SetClientForTesting's pattern) fakes
        // Helpers.cardCast.sendPOST's single chokepoint - getPackCards calls it twice per import
        // (pack info, then "<code>/cards"), so the fake switches on the URL suffix.
        using var bot = StartThreePlayerGame();
        cardCast.HttpGetOverride = url => url.EndsWith("/cards")
            ? """{ "calls": [ { "text": ["Roses are red, ", "."] } ], "responses": [ { "text": ["violets"] }, { "text": ["daisies"] } ] }"""
            : """{ "name": "Test Pack", "description": "A pack for testing" }""";

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_settings", "Alice");
        bot.TapButton(Alice, "Change Packs", "Alice");
        bot.TapButton(Alice, "Import Pack", "Alice");
        bot.TapButton(Alice, "TESTCODE", "Alice");

        var xyzzy = Plugins.plugins.OfType<mod_xyzzy>().Single();
        var coreData = (mod_xyzzy_coredata)xyzzy.getPluginData();
        Assert.Contains(coreData.packs, p => p.name == "Test Pack");
        Assert.Contains(coreData.questions, q => q.text.Contains("Roses are red"));
        Assert.Contains(coreData.answers, a => a.text == "violets");
        Assert.Contains(coreData.answers, a => a.text == "daisies");

        var importedPack = coreData.packs.Single(p => p.name == "Test Pack");
        Assert.True(ChatData().packEnabled(importedPack.packID));
        Assert.Contains(bot.BotClient.SentMessages, m => m.Text.Contains("Importing fresh pack"));
    }

    [Fact]
    public void CardCastImportWithAnInvalidCodeRepromptsWithoutCrashing()
    {
        using var bot = StartThreePlayerGame();
        // getPackCards' own regex check on the pack code runs before any network call, so an empty/
        // malformed override response is never actually parsed here - this exercises the "the API
        // call itself reported failure" retry path (importMessage set, success:false), not a parse
        // error.
        cardCast.HttpGetOverride = url => "null";

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_settings", "Alice");
        bot.TapButton(Alice, "Change Packs", "Alice");
        bot.TapButton(Alice, "Import Pack", "Alice");
        bot.TapButton(Alice, "BADCODE", "Alice");

        Assert.Contains("Couldn't add the pack", bot.BotClient.SentMessages[^1].Text);
        Assert.Contains("enter the pack code", bot.BotClient.SentMessages[^1].Text);
    }
}
