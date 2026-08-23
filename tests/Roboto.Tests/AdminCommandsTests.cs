using System.Linq;
using RobotoChatBot;
using RobotoChatBot.Modules;

namespace RobotoTests;

public class AdminCommandsTests
{
    private const long ChatId = -100;
    private const long Alice = 1;
    private const long Bob = 2;

    [Fact]
    public void VersionReportsTheGitCommitAndBuildDateBakedIntoTheAssembly()
    {
        // Answers "which build is actually running" against a real deployed instance - GitCommit/
        // BuildDate come from Roboto.csproj's own MSBuild-time AssemblyMetadata (BuildInfo.cs reads
        // them via reflection), not anything runtime-configurable, so this just proves the plumbing
        // reaches the chat rather than asserting a specific commit value.
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/version", "Alice");

        var text = bot.BotClient.SentMessages[^1].Text;
        Assert.Contains("Git commit: " + BuildInfo.GitCommit, text);
        Assert.Contains("Built: " + BuildInfo.BuildDate, text);
    }

    [Fact]
    public void StopMutesTheChatAndStartUnmutesIt()
    {
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/stop", "Alice");
        Assert.Contains("ignoring all messages", bot.BotClient.SentMessages[^1].Text);

        bot.SendGroupMessage(ChatId, Alice, "/start", "Alice");
        Assert.Contains("listening for messages again", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public void MutingAChatSuppressesModulesThatArentExemptFromMuting()
    {
        // mod_standard itself is exempt from muting (chatIfMuted=true, so /start/stop/addadmin etc
        // keep working) - mod_xyzzy is not, so it's the one that actually proves muting suppresses a
        // module's chatEvent.
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/stop", "Alice");
        var beforeCount = bot.BotClient.SentMessages.Count;

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_status", "Alice");

        Assert.Equal(beforeCount, bot.BotClient.SentMessages.Count);
    }

    [Fact]
    public void AddAdminWithNoExistingAdminsMakesTheCallerAdmin()
    {
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/addadmin", "Alice");

        Assert.Contains("admin", bot.BotClient.SentMessages[^1].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HelpDoesNothingWhileMuted()
    {
        using var bot = new TestHarness();
        bot.SendGroupMessage(ChatId, Alice, "/stop", "Alice");
        var beforeCount = bot.BotClient.SentMessages.Count;

        bot.SendGroupMessage(ChatId, Alice, "/help", "Alice");

        // /help has its own explicit `c.muted == false` gate (unlike /start/stop themselves,
        // mod_standard's chatIfMuted exemption only keeps its *chatEvent* running while muted - it
        // doesn't mean every command inside ignores mute individually).
        Assert.Equal(beforeCount, bot.BotClient.SentMessages.Count);
    }

    [Fact]
    public void StartWhileNotMutedSendsTheDefaultWelcomeMessage()
    {
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/start", "Alice");

        Assert.Equal(ChatId, bot.BotClient.SentMessages[^1].ChatId);
        Assert.NotEmpty(bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public void QuietHoursFullFlowSetsStartAndEndTimes()
    {
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/setquiethours", "Alice");
        Assert.Contains("start time", bot.BotClient.SentMessages[^1].Text);

        bot.TapButton(Alice, "23:00:00", "Alice");
        Assert.Contains("wake time", bot.BotClient.SentMessages[^1].Text);

        bot.TapButton(Alice, "07:00:00", "Alice");

        var chatData = (mod_standard_chatdata)Chats.getChat(ChatId).getPluginData(typeof(mod_standard_chatdata));
        Assert.Equal(TimeSpan.Parse("23:00:00"), chatData.quietHoursStartTime);
        Assert.Equal(TimeSpan.Parse("07:00:00"), chatData.quietHoursEndTime);
    }

    [Fact]
    public void QuietHoursDisableClearsBothTimes()
    {
        using var bot = new TestHarness();
        bot.SendGroupMessage(ChatId, Alice, "/setquiethours", "Alice");
        bot.TapButton(Alice, "23:00:00", "Alice");
        bot.TapButton(Alice, "07:00:00", "Alice");

        bot.SendGroupMessage(ChatId, Alice, "/setquiethours", "Alice");
        bot.TapButton(Alice, "disable", "Alice");

        var chatData = (mod_standard_chatdata)Chats.getChat(ChatId).getPluginData(typeof(mod_standard_chatdata));
        Assert.Equal(TimeSpan.MinValue, chatData.quietHoursStartTime);
        Assert.Equal(TimeSpan.MinValue, chatData.quietHoursEndTime);
    }

    [Fact]
    public void QuietHoursInvalidValueReprompts()
    {
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/setquiethours", "Alice");
        bot.TapButton(Alice, "not a time", "Alice");

        Assert.Contains("Invalid value", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public void AddAdminWithExistingAdminsPicksFromRecentChatMembers()
    {
        // Bob needs a real Presence entry tagged with this group's chatID before he shows up as a
        // pickable option - Presence.markPresence only fires as a side effect of a SendQuestion
        // targeting (groupChatId, userId, isPrivateMessage:true), not from ordinary incoming group
        // messages (confirmed against both this branch and legacy directly). A full xyzzy round
        // start naturally produces exactly that (Bob's own hand-selection DM), so it's used here
        // purely as a way to get Bob into the recent-members list, not because it's otherwise
        // relevant to /addadmin. Bob shows up as "(151)" rather than "Bob(151)" - askQuestion's
        // per-player SendQuestion call passes userName:null, so chatPresence.ToString() has nothing
        // to prefix; a real, harmless, pre-existing quirk, not something introduced by this test.
        using var bot = new TestHarness();
        var xyzzy = Plugins.plugins.OfType<mod_xyzzy>().Single();
        var coreData = (mod_xyzzy_coredata)xyzzy.getPluginData();
        coreData.questions.Add(new mod_xyzzy_card("Q ___?", mod_xyzzy.primaryPackID, 1));
        for (int i = 0; i < 40; i++) { coreData.answers.Add(new mod_xyzzy_card("A" + i, mod_xyzzy.primaryPackID)); }

        bot.SendGroupMessage(ChatId, Alice, "/addadmin", "Alice");
        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_start", "Alice");
        bot.TapButton(Alice, "Use Defaults", "Alice");
        bot.SendGroupMessage(ChatId, Bob, "/xyzzy_join", "Bob");
        bot.SendGroupMessage(ChatId, 3, "/xyzzy_join", "Carol");
        bot.TapButton(Alice, "Start", "Alice");

        bot.SendGroupMessage(ChatId, Alice, "/addadmin", "Alice");
        string bobsButton = bot.LastKeyboardMessageTo(Alice).KeyboardRows!.SelectMany(r => r).Single(b => b.Text.Contains("(" + Bob + ")")).Text;

        bot.TapButton(Alice, bobsButton, "Alice");

        Assert.Contains("Successfully added admin", bot.BotClient.SentMessages[^1].Text);
        Assert.True(Chats.getChat(ChatId).isChatAdmin(Bob));
    }

    [Fact]
    public void RemoveAdminRemovesThePickedAdmin()
    {
        using var bot = new TestHarness();
        bot.SendGroupMessage(ChatId, Alice, "/addadmin", "Alice");

        bot.SendGroupMessage(ChatId, Alice, "/removeadmin", "Alice");
        string aliceButton = bot.LastKeyboardMessageTo(Alice).KeyboardRows!.SelectMany(r => r).Single(b => b.Text == Alice.ToString()).Text;

        bot.TapButton(Alice, aliceButton, "Alice");

        Assert.Contains("Successfully removed admin", bot.BotClient.SentMessages[^1].Text);
        Assert.DoesNotContain(Alice, Chats.getChat(ChatId).chatAdmins);
    }

    [Fact]
    public void RemoveAdminWithNoAdminsReportsSo()
    {
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/removeadmin", "Alice");

        Assert.Contains("doesnt have any admins", bot.BotClient.SentMessages[^1].Text);
    }
}
