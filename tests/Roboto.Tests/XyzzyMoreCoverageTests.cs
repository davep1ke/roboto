using System;
using System.Linq;
using RobotoChatBot;
using RobotoChatBot.Helpers;
using RobotoChatBot.Modules;

namespace RobotoTests;

/// <summary>
/// Further mod_xyzzy coverage beyond XyzzyGameFlowTests/XyzzySettingsTests: timeout/delay settings,
/// the background check() timeout-skip path (deterministic via a backdated statusChangedTime rather
/// than waiting on real wall-clock time), pack-list pagination Next/Prev, and both /xyzzy_leave
/// variants (in-group and the DM multi-game picker).
/// </summary>
public class XyzzyMoreCoverageTests
{
    private const long ChatId = -1100;
    private const long Alice = 110;
    private const long Bob = 111;
    private const long Carol = 112;

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
    public void TimeoutSettingFlowUpdatesMaxAndMinWaitHours()
    {
        using var bot = StartThreePlayerGame();

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_settings", "Alice");
        bot.TapButton(Alice, "Timeout", "Alice");
        bot.TapButton(Alice, "6", "Alice");

        var chatData = ChatData();
        Assert.Equal(6, chatData.maxWaitTimeHours);

        // Setting Timeout already redisplays the Settings menu on its own (setMaxHours's handler
        // calls sendSettingsMessage once it's not part of the initial setup wizard) - tap "Delay" on
        // that, rather than sending a second /xyzzy_settings, which would create a second
        // simultaneously-outstanding Settings prompt for Alice and misroute the next reply (same
        // interaction XyzzyCarriedForwardDeltasTests' Abandon tests had to work around).
        bot.TapButton(Alice, "Delay", "Alice");
        bot.TapButton(Alice, "2", "Alice");

        Assert.Equal(2, chatData.minWaitTimeHours);
    }

    [Fact]
    public void BackgroundCheckSkipsOutstandingPlayersOnceThePastTheAbandonTimeout()
    {
        using var bot = StartThreePlayerGame();
        var chatData = ChatData();
        Assert.Equal(xyzzy_Statuses.Question, chatData.status);

        // Carol answers, Bob doesn't - beginJudging needs at least one real submission or it just
        // deals a fresh round instead (possibleAnswerCount == 0 -> "Not enough answers to judge!
        // Skipping to next question" -> askQuestion(true) -> status back to Question, which is what
        // happened here on the first attempt at this test with nobody having answered at all).
        string carolsAnswer = bot.LastKeyboardMessageTo(Carol).KeyboardRows![0][0].Text;
        bot.TapButton(Carol, carolsAnswer, "Carol");

        // Backdate statusChangedTime and set a 1-hour timeout so check()'s abandonTime math
        // (statusChangedTime + maxWaitTimeHours) is already in the past - deterministic, rather
        // than waiting on real wall-clock time. Quick check (fullCheck: false), not full: a full
        // check also asks TelegramAPI.getChatMembersCount, which FakeTelegramBotClient always
        // answers "1" (its GetChatMemberCountRequest case is a fixed stub, not a real member list)
        // - legacy reads a count of 1 as "everyone but the bot has left" and abandons the game
        // outright, unrelated to the timeout behavior under test here (which lives outside the
        // `if (fullCheck)` block regardless).
        chatData.maxWaitTimeHours = 1;
        chatData.statusChangedTime = DateTime.Now.AddHours(-2);

        chatData.check(false);

        Assert.Equal(xyzzy_Statuses.Judging, chatData.status);
        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("suck it for not answering in time"));
    }

    [Fact]
    public void PackListPaginationNextAndPrevNavigateBetweenPages()
    {
        using var bot = StartThreePlayerGame();
        var xyzzy = Plugins.plugins.OfType<mod_xyzzy>().Single();
        var coreData = (mod_xyzzy_coredata)xyzzy.getPluginData();
        for (int i = 0; i < 35; i++)
        {
            coreData.packs.Add(new cardcast_pack("Pack " + i, "code" + i, "desc"));
        }

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_settings", "Alice");
        bot.TapButton(Alice, "Change Packs", "Alice");
        Assert.Contains("(Page 1 of 2)", bot.BotClient.SentMessages[^1].Text);

        bot.TapButton(Alice, "Next", "Alice");
        Assert.Contains("(Page 2 of 2)", bot.BotClient.SentMessages[^1].Text);

        bot.TapButton(Alice, "Prev", "Alice");
        Assert.Contains("(Page 1 of 2)", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public void LeaveFromTheGroupRemovesThePlayer()
    {
        using var bot = StartThreePlayerGame();

        bot.SendGroupMessage(ChatId, Bob, "/xyzzy_leave", "Bob");

        var chatData = ChatData();
        Assert.DoesNotContain(chatData.players, p => p.name.Trim() == "Bob");
    }

    [Fact]
    public void LeaveFromADmPicksWhichGameToLeaveWhenInMultipleGames()
    {
        // Stops short of tapping "Start" (unlike StartThreePlayerGame) - once a round is dealt, Bob
        // already has his own outstanding hand-selection DM prompt, and legacy's per-user
        // single-outstanding-question queue would then just queue the leave-picker prompt behind it
        // instead of delivering it immediately (SendQuestion's trySendImmediately:true only jumps
        // the queue if there's no already-*sent* question awaiting an answer - see Messaging.
        // processNewExpectedReply). Bob just being a joined player, before Start, is enough to
        // appear in the leave picker.
        using var bot = new TestHarness();
        SeedCards();
        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_start", "Alice");
        bot.TapButton(Alice, "Use Defaults", "Alice");
        bot.TapButton(Alice, "Add Bots", "Alice"); // Use Defaults now auto-adds 2 bots - clear them for a clean human-only baseline
        bot.TapButton(Alice, "Remove All Bots", "Alice");
        bot.SendGroupMessage(ChatId, Bob, "/xyzzy_join", "Bob");
        bot.SendGroupMessage(ChatId, Carol, "/xyzzy_join", "Carol");

        bot.SendPrivateMessage(Bob, "/xyzzy_leave", "Bob");
        var keyboard = bot.LastKeyboardMessageTo(Bob).KeyboardRows!;
        string gameButton = keyboard.SelectMany(r => r).Single(b => b.Text.StartsWith("Test Group")).Text;

        bot.TapButton(Bob, gameButton, "Bob");

        var chatData = ChatData();
        Assert.DoesNotContain(chatData.players, p => p.name.Trim() == "Bob");
    }

    [Fact]
    public void LeaveFromADmWithNoActiveGamesSaysSo()
    {
        using var bot = new TestHarness();

        bot.SendPrivateMessage(Alice, "/xyzzy_leave", "Alice");

        Assert.Contains("not in any active games", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public void StatusDuringAQuestionRoundHandlesTheCurrentCardHavingBeenRemoved()
    {
        // Real production crash (2026-08-24): /xyzzy_status mid-round threw a NullReferenceException
        // and the user got no response at all. getQuestionCard(currentQuestion) returns null once the
        // card the round is currently on is no longer in the catalog - e.g. its pack got
        // dropped/disabled after the round started - and getStatus() dereferenced .text on that
        // straight away with no guard. Simulated here by clearing the whole catalog out from under an
        // in-progress round, same "card vanished mid-game" shape as the live incident.
        using var bot = StartThreePlayerGame();
        var chatData = ChatData();
        Assert.Equal(xyzzy_Statuses.Question, chatData.status);

        var coreData = (mod_xyzzy_coredata)Plugins.plugins.OfType<mod_xyzzy>().Single().getPluginData();
        coreData.questions.Clear();

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_status", "Alice");

        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("no longer available"));
    }
}
