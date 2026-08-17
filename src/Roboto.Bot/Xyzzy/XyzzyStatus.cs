namespace Roboto.Bot.Xyzzy;

public enum XyzzyStatus
{
    Stopped,
    Invites,
    Question,
    Judging,

    /// <summary>Between hands - a round just finished but the next question is being held back by
    /// MinWaitHours and/or quiet hours (phase 8.3). XyzzyRoundReconciler moves this back to Question
    /// once both clear; see XyzzyRoundService.AdvanceToNextHandAsync.</summary>
    WaitingForNextHand,
}
