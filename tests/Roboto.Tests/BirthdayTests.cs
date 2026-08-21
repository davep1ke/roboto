using System.Linq;
using RobotoChatBot;
using RobotoChatBot.Modules;

namespace RobotoTests;

public class BirthdayTests
{
    private const long ChatId = -400;
    private const long Alice = 30;

    [Fact]
    public void AddThenListABirthday()
    {
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/birthday_add", "Alice");
        Assert.Contains("Whose birthday", bot.BotClient.SentMessages[^1].Text);

        bot.TapButton(Alice, "Bob", "Alice");
        Assert.Contains("What is their Birthday", bot.BotClient.SentMessages[^1].Text);

        bot.TapButton(Alice, "01-JAN-1990", "Alice");
        Assert.Contains("Added Bob's Birthday (1990-01-01)", bot.BotClient.SentMessages[^1].Text);

        var chatData = (mod_birthday_data)Chats.getChat(ChatId).getPluginData(typeof(mod_birthday_data));
        Assert.Contains(chatData.birthdays, b => b.name == "Bob");
    }

    [Fact]
    public void RemoveABirthday()
    {
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/birthday_add", "Alice");
        bot.TapButton(Alice, "Bob", "Alice");
        bot.TapButton(Alice, "01-JAN-1990", "Alice");

        bot.SendGroupMessage(ChatId, Alice, "/birthday_remove", "Alice");
        bot.TapButton(Alice, "Bob", "Alice");
        Assert.Contains("Removed birthday for Bob successfully", bot.BotClient.SentMessages[^1].Text);

        var chatData = (mod_birthday_data)Chats.getChat(ChatId).getPluginData(typeof(mod_birthday_data));
        Assert.DoesNotContain(chatData.birthdays, b => b.name == "Bob");
    }
}
