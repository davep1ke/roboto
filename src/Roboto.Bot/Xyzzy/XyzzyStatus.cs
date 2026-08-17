namespace Roboto.Bot.Xyzzy;

public enum XyzzyStatus
{
    Stopped,

    /// <summary>Between /xyzzy_start and Invites - the starter is being asked (over DM, phase 8.5)
    /// whether to use default settings or configure question-limit/timeout/throttle first. A game
    /// stuck here for 24h is auto-reset by XyzzyRoundReconciler, mirroring legacy's "idle setup
    /// auto-resets" behavior.</summary>
    SettingUp,

    Invites,
    Question,
    Judging,

    /// <summary>Between hands - a round just finished but the next question is being held back by
    /// MinWaitHours and/or quiet hours (phase 8.3). XyzzyRoundReconciler moves this back to Question
    /// once both clear; see XyzzyRoundService.AdvanceToNextHandAsync.</summary>
    WaitingForNextHand,
}
