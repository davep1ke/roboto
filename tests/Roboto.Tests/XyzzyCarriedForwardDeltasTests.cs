using System.Linq;
using RobotoChatBot;
using RobotoChatBot.Helpers;
using RobotoChatBot.Modules;

namespace RobotoTests;

/// <summary>
/// Covers phase 9's carried-forward deltas from the abandoned rewrite branch (MIGRATION.md phase 9):
/// the real Abandon confirm and pack-list-pagination bug fixes (both genuine legacy bugs, confirmed
/// present in legacy-winforms-baseline before being ported here as fixes), bot self-de-admin (a
/// wholly new feature - legacy never reacted to its own admin status), "Add Bots" (also wholly new -
/// legacy never had non-human players), and judge-kick/leave (a deliberate no-op: confirmed legacy's
/// existing re-pick-a-judge-for-the-same-round behavior already works and was kept as-is rather than
/// adopting the rewrite's simplification, so this just proves that existing behavior).
/// </summary>
public class XyzzyCarriedForwardDeltasTests
{
    private const long ChatId = -700;
    private const long Alice = 70;
    private const long Bob = 71;
    private const long Carol = 72;

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

    [Fact]
    public void AbandonConfirmOnlyAbandonsOnYes()
    {
        using var bot = new TestHarness();
        SeedCards();

        // Full 3-player start rather than stopping at the Invites screen: Alice (the starter, so
        // players[0]/tzar) gets a plain SendMessage for her own question, not a SendQuestion - she
        // has no outstanding DM reply pending, so /xyzzy_settings -> Abandon isn't racing an
        // earlier unresolved prompt for the same user (legacy's per-user single-outstanding-question
        // queue would otherwise route the "Abandon" reply to whichever prompt is still open, not
        // necessarily the Settings one - a pre-existing interaction, not what this test is about).
        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_start", "Alice");
        bot.TapButton(Alice, "Use Defaults", "Alice");
        bot.SendGroupMessage(ChatId, Bob, "/xyzzy_join", "Bob");
        bot.SendGroupMessage(ChatId, Carol, "/xyzzy_join", "Carol");
        bot.TapButton(Alice, "Start", "Alice");

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_settings", "Alice");
        bot.TapButton(Alice, "Abandon", "Alice");
        Assert.Contains("Are you sure", bot.BotClient.SentMessages[^1].Text);

        bot.TapButton(Alice, "No", "Alice");

        Assert.Contains("Not abandoned.", bot.BotClient.SentMessages[^1].Text);
        var chatData = (mod_xyzzy_chatdata)Chats.getChat(ChatId).getPluginData(typeof(mod_xyzzy_chatdata), true);
        Assert.NotEqual(xyzzy_Statuses.Stopped, chatData.status);
    }

    [Fact]
    public void AbandonConfirmAbandonsOnYes()
    {
        using var bot = new TestHarness();
        SeedCards();

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_start", "Alice");
        bot.TapButton(Alice, "Use Defaults", "Alice");
        bot.SendGroupMessage(ChatId, Bob, "/xyzzy_join", "Bob");
        bot.SendGroupMessage(ChatId, Carol, "/xyzzy_join", "Carol");
        bot.TapButton(Alice, "Start", "Alice");

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_settings", "Alice");
        bot.TapButton(Alice, "Abandon", "Alice");
        bot.TapButton(Alice, "Yes", "Alice");

        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("Game abandoned"));
        var chatData = (mod_xyzzy_chatdata)Chats.getChat(ChatId).getPluginData(typeof(mod_xyzzy_chatdata), true);
        Assert.Equal(xyzzy_Statuses.Stopped, chatData.status);
    }

    [Fact]
    public void PackListPaginationHasNoPhantomTrailingPageOnAnExactMultiple()
    {
        using var bot = new TestHarness();
        var xyzzy = Plugins.plugins.OfType<mod_xyzzy>().Single();
        var coreData = (mod_xyzzy_coredata)xyzzy.getPluginData();
        coreData.packs.Clear();
        // Exactly maxPacksPerPage (30) packs - the old (count / maxPacksPerPage) + 1 formula would
        // report 2 pages here even though page 2 has zero pack buttons on it.
        for (int i = 0; i < 30; i++)
        {
            coreData.packs.Add(new cardcast_pack("Pack " + i, "code" + i, "desc"));
        }

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_start", "Alice");
        var chatData = (mod_xyzzy_chatdata)Chats.getChat(ChatId).getPluginData(typeof(mod_xyzzy_chatdata), true);

        chatData.sendPackFilterMessage(new message(TestHarness.PrivateMessage(Alice, "irrelevant")), 1);

        var lastMessage = bot.BotClient.SentMessages[^1];
        Assert.DoesNotContain("Page 1 of 2", lastMessage.Text);
        Assert.DoesNotContain("(Page", lastMessage.Text);
    }

    [Fact]
    public void PromotingTheBotToAdminStripsItsRightsBackOffAndExplainsWhy()
    {
        using var bot = new TestHarness();

        bot.PromoteBotToAdmin(ChatId);

        Assert.Contains((ChatId, bot.BotClient.BotId), bot.BotClient.PromoteChatMemberCalls);
        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text == TelegramAPI.BotSelfDeAdminExplanation);
    }

    [Fact]
    public void AddBotsLetsASoloStarterReachThreePlayersAndPlayARound()
    {
        using var bot = new TestHarness();
        SeedCards();

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_start", "Alice");
        bot.TapButton(Alice, "Use Defaults", "Alice");

        // Solo - just Alice so far. Add 2 bots to reach 3 players without needing Bob/Carol.
        bot.TapButton(Alice, "Add Bots", "Alice");
        Assert.Contains("How many bots", bot.BotClient.SentMessages[^1].Text);
        bot.TapButton(Alice, "2", "Alice");

        var chatData = (mod_xyzzy_chatdata)Chats.getChat(ChatId).getPluginData(typeof(mod_xyzzy_chatdata), true);
        Assert.Equal(3, chatData.players.Count);
        Assert.Equal(2, chatData.players.Count(p => p.isBot));

        bot.TapButton(Alice, "Start", "Alice");

        // Alice is players[0] (the starter), so she's tzar - both bots are non-judge and answer
        // immediately without ever being DMed (their playerIDs are negative, -1/-2).
        Assert.Equal(xyzzy_Statuses.Judging, chatData.status);
        Assert.DoesNotContain(bot.BotClient.SentMessages, m => m.ChatId == -1 || m.ChatId == -2);

        // Both bots must actually get a turn to answer, not just "judging eventually starts" -
        // logAnswer's old outstandingResponses()-based completion check couldn't see bot players at
        // all (they never get a real ExpectedReply/DM), so with zero other real players it read
        // "everyone's answered" the instant the *first* bot in the auto-answer loop submitted,
        // before the second bot's turn - which this test's earlier version didn't catch, since it
        // only asserted Judging was reached and *a* winner could be picked, not that both bots'
        // answers were actually collected. Found via a live round-trip against the beefy test bot.
        var judgingKeyboard = bot.LastKeyboardMessageTo(Alice).KeyboardRows!;
        Assert.Equal(2, judgingKeyboard.SelectMany(r => r).Count());
        Assert.DoesNotContain(bot.BotClient.SentMessages, m => m.Text.Contains("Skipped these chumps"));

        string winningAnswer = judgingKeyboard[0][0].Text;
        bot.TapButton(Alice, winningAnswer, "Alice");

        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("wins a point!"));
        var winner = chatData.players.Single(p => p.wins == 1);
        Assert.True(winner.isBot);

        // Round 2 auto-starts with lastPlayerAsked rotated to players[1] - the first bot added -
        // making it the judge. Alice (players[0]) and the second bot (players[2]) are non-judge;
        // the second bot auto-answers immediately same as round 1, so only Alice's real answer is
        // needed to complete the round and trigger the bot judge's auto-pick.
        //
        // beginJudging's bot-judge branch (tzar.isBot) used to return before ever sending the
        // group's "All answers received!" announcement - only the human-judge path sent it, so with
        // a bot judge the chat went straight from "everyone's answered" to "a winner's been picked"
        // with no announcement of what the answers even were in between. Found via a live user
        // report (round 2 of a real game, not round 1 - round 1's judge here is always human Alice,
        // the starter, so this needed a second round to surface at all).
        Assert.Equal(xyzzy_Statuses.Question, chatData.status);
        Assert.Equal(1, chatData.lastPlayerAsked);
        string alicesAnswer = bot.LastKeyboardMessageTo(Alice).KeyboardRows![0][0].Text;
        bot.TapButton(Alice, alicesAnswer, "Alice");

        Assert.Equal(2, bot.BotClient.SentMessages.Count(m => m.ChatId == ChatId && m.Text.Contains("All answers received!")));
        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("wins a point!"));
    }

    [Fact]
    public void SettingsMenuQueuedBehindAnOutstandingAnswerSurvivesIntoTheNextRound()
    {
        // Legacy's own TODO in askQuestion() ("this causes issues if someone is changing settings
        // in the middle of a round") called this exact bug out, unfixed since. Reproduces the real
        // live shape: Alice requests /xyzzy_settings while she still has an outstanding "Question"
        // reply for round 2 (bot1 is judge here - lastPlayerAsked rotates to players[1] after round
        // 1) - Messaging's per-user single-outstanding-message queue holds the Settings reply
        // behind it, unsent. Answering completes the round, triggering bot1's auto-judge (no human
        // wait) and dealing round 3 immediately - askQuestion()'s clearExpectedReplies used to
        // blanket-clear every mod_xyzzy ExpectedReply for the chat at that point, silently deleting
        // the still-queued Settings reply before it ever got a chance to send.
        using var bot = new TestHarness();
        SeedCards();

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_start", "Alice");
        bot.TapButton(Alice, "Use Defaults", "Alice");
        bot.TapButton(Alice, "Add Bots", "Alice");
        bot.TapButton(Alice, "2", "Alice");
        bot.TapButton(Alice, "Start", "Alice");

        var chatData = (mod_xyzzy_chatdata)Chats.getChat(ChatId).getPluginData(typeof(mod_xyzzy_chatdata), true);
        // Round 1: Alice (players[0], the starter) is tzar - judge immediately so round 2 starts.
        var judgingKeyboard = bot.LastKeyboardMessageTo(Alice).KeyboardRows!;
        bot.TapButton(Alice, judgingKeyboard[0][0].Text, "Alice");
        Assert.Equal(1, chatData.lastPlayerAsked);

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_settings", "Alice");
        Assert.DoesNotContain(bot.BotClient.SentMessages, m => m.Text.Contains("This allows you to change the game settings"));

        string alicesAnswer = bot.LastKeyboardMessageTo(Alice).KeyboardRows![0][0].Text;
        bot.TapButton(Alice, alicesAnswer, "Alice");

        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == Alice && m.Text.Contains("This allows you to change the game settings"));
    }

    [Fact]
    public void KickingTheJudgeMidJudgingReassignsTheSameRoundRatherThanDealingAFreshOne()
    {
        // Confirms the deliberate no-op decision (MIGRATION.md phase 9): legacy's real behavior -
        // re-pick a judge and resume judging on the same round's already-collected answers - was
        // kept as-is rather than adopting the rewrite's "deal a fresh round" simplification.
        using var bot = new TestHarness();
        SeedCards();

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_start", "Alice");
        bot.TapButton(Alice, "Use Defaults", "Alice");
        bot.SendGroupMessage(ChatId, Bob, "/xyzzy_join", "Bob");
        bot.SendGroupMessage(ChatId, Carol, "/xyzzy_join", "Carol");
        bot.TapButton(Alice, "Start", "Alice");

        var chatData = (mod_xyzzy_chatdata)Chats.getChat(ChatId).getPluginData(typeof(mod_xyzzy_chatdata), true);
        string bobsAnswer = bot.LastKeyboardMessageTo(Bob).KeyboardRows![0][0].Text;
        string carolsAnswer = bot.LastKeyboardMessageTo(Carol).KeyboardRows![0][0].Text;
        bot.TapButton(Bob, bobsAnswer, "Bob");
        bot.TapButton(Carol, carolsAnswer, "Carol");

        Assert.Equal(xyzzy_Statuses.Judging, chatData.status);
        string currentQuestionBeforeKick = chatData.currentQuestion;
        long judgeID = chatData.players[chatData.lastPlayerAsked].playerID;
        Assert.Equal(Alice, judgeID);

        chatData.removePlayer(Alice);

        // Same round: still Judging, same question, still 2 players (Bob and Carol), and the new
        // judge (whoever's now at lastPlayerAsked) got a fresh judging DM re-using the same
        // already-submitted answers - not a fresh Question round dealt from scratch.
        Assert.Equal(xyzzy_Statuses.Judging, chatData.status);
        Assert.Equal(currentQuestionBeforeKick, chatData.currentQuestion);
        Assert.Equal(2, chatData.players.Count);
        Assert.Contains(bot.BotClient.SentMessages, m => m.Text.Contains("Judge") && m.Text.Contains("judge is now"));

        long newJudgeID = chatData.players[chatData.lastPlayerAsked].playerID;
        Assert.NotEqual(Alice, newJudgeID);
        var judgingKeyboard = bot.LastKeyboardMessageTo(newJudgeID).KeyboardRows!;
        Assert.Contains(new[] { bobsAnswer, carolsAnswer }, a => judgingKeyboard.Any(row => row.Any(btn => btn.Text == a)));
    }
}
