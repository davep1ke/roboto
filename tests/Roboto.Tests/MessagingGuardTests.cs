using System;
using RobotoChatBot;
using RobotoChatBot.Modules;

namespace RobotoTests;

/// <summary>
/// Covers the guard added to Messaging.processNewExpectedReply (MIGRATION.md phase 9 addendum):
/// a group-targeted question (isPrivateMessage:false, expectsReply:true) never actually registers
/// the ExpectedReply for matching - a real, confirmed-live legacy bug (byte-for-byte present in
/// legacy-winforms-baseline) that left mod_quote's /quote_config and mod_steam's /steam_addplayer
/// completely non-functional. Every real call site was migrated to isPrivateMessage:true instead of
/// teaching this branch to also queue, so this path should never be hit in practice any more - the
/// guard exists to fail loudly if it ever is (a future call site making the same mistake), rather
/// than silently swallowing the reply the way it used to.
/// </summary>
public class MessagingGuardTests
{
    [Fact]
    public void GroupTargetedQuestionThrowsInsteadOfSilentlyLosingTheReply()
    {
        using var bot = new TestHarness();

        Assert.Throws<NotImplementedException>(() =>
            Messaging.SendQuestion(-1400, 140, "This should never be sent this way", false, typeof(mod_standard), "SOME_STATE"));
    }

    [Fact]
    public void GroupMessageWithNoReplyExpectedIsUnaffectedByTheGuard()
    {
        // SendMessage always constructs its ExpectedReply with expectsReply:false - the guard only
        // fires for expectsReply:true, so ordinary group messages (the overwhelming majority of
        // traffic) must keep working exactly as before.
        using var bot = new TestHarness();

        long messageId = Messaging.SendMessage(-1400, "Just a notice, not a question");

        Assert.True(messageId > 0);
    }
}
