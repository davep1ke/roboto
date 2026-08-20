namespace Roboto.Bot.Tests.Xyzzy;

/// <summary>Covers a real bug reported live: a player in two simultaneous xyzzy games had no way to
/// tell which game a given "Pick a card" DM belonged to. Legacy stamped a per-user DM with the
/// originating chat's title whenever the recipient was active in more than one chat at once
/// (TelegramAPI.postExpectedReplyToPlayer's Presence-based check) - this rewrite substitutes "how
/// many active xyzzy games is this player currently in" for presence tracking (dropped from this
/// port entirely, nothing else needs it) as the equivalent signal. See
/// XyzzyRoundService.StampChatAsync.</summary>
public class XyzzyMultiGameDmStampTests
{
    private const long ChatOne = -910;
    private const long ChatTwo = -920;
    private const long Alice = 1;
    private const long Bob = 2;
    private const long Carol = 3;

    private static async Task StartAndBeginAsync(TestBot bot, long chatId, long starterId, string starterName, long joinerId, string joinerName)
    {
        await bot.SendAsync(TestBot.GroupMessage(chatId, starterId, "/xyzzy_start", firstName: starterName));
        var choiceMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == starterId && m.Buttons is { Count: > 0 });
        await bot.SendCallbackAsync(starterId, choiceMessage.Buttons!.First(b => b.Text == "Use Defaults"));

        await bot.SendAsync(TestBot.GroupMessage(chatId, joinerId, "/xyzzy_join", firstName: joinerName));

        var startMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == starterId && m.Buttons is { Count: > 0 } && m.Buttons.Any(b => b.Text == "Start"));
        await bot.SendCallbackAsync(starterId, startMessage.Buttons!.First(b => b.Text == "Start"));
    }

    [Fact]
    public async Task AHandKeyboardIsStampedWithTheChatOnlyOnceThePlayerHasASecondActiveGame()
    {
        using var bot = new TestBot();

        // Game One: Alice starts, Bob joins (FillBotSlots tops up to 3) - round 1's judge is
        // always Players[0] (Alice, the starter), so Bob gets the hand-keyboard DM, not the
        // judging notice. Bob is only in one active game at this point - unstamped.
        await StartAndBeginAsync(bot, ChatOne, Alice, "Alice", Bob, "Bob");
        var gameOneHand = bot.BotClient.SentMessages.Last(m => m.ChatId == Bob && m.Buttons is { Count: > 0 });
        Assert.DoesNotContain("=>", gameOneHand.Text);

        // Game Two: Carol starts, Bob joins too - now Bob is a player in two active games at
        // once. Bob's DM queue already has Game One's still-unanswered hand keyboard as its head
        // (delivered, but unresolved), so Game Two's hand keyboard queues behind it rather than
        // being sent immediately - the stamp is baked into the text at enqueue time regardless.
        await StartAndBeginAsync(bot, ChatTwo, Carol, "Carol", Bob, "Bob");

        // Still Game One's message - Game Two's hasn't been delivered yet.
        Assert.DoesNotContain("=>", bot.BotClient.SentMessages.Last(m => m.ChatId == Bob).Text);

        // Resolving Game One's card pumps Bob's queue - Game Two's hand keyboard is delivered
        // next, and should be stamped since Bob is (still) in two active games.
        await bot.AnswerHandFullyAsync(Bob);

        var gameTwoHand = bot.BotClient.SentMessages.Last(m => m.ChatId == Bob && m.Buttons is { Count: > 0 });
        Assert.StartsWith($"=> {ChatTwo}\n", gameTwoHand.Text);
    }
}
