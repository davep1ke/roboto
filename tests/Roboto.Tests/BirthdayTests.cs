using System;
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

    [Fact]
    public void BackgroundProcessingAnnouncesABirthdayFallingOnToday()
    {
        using var bot = new TestHarness();
        bot.SendGroupMessage(ChatId, Alice, "/birthday_add", "Alice");
        bot.TapButton(Alice, "Bob", "Alice");
        bot.TapButton(Alice, "01-JAN-1990", "Alice");

        // Year is irrelevant to backgroundProcessing's day/month match - override to today's real
        // day/month (keeping a synthetic year) rather than patching the clock.
        var chatData = (mod_birthday_data)Chats.getChat(ChatId).getPluginData(typeof(mod_birthday_data));
        chatData.birthdays.Single(b => b.name == "Bob").birthday = new DateTime(1990, DateTime.Now.Month, DateTime.Now.Day);

        bot.RunBackgroundProcessing();

        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text == "Happy Birthday to Bob!");
    }

    [Fact]
    public void BackgroundProcessingDoesNotAnnounceABirthdayOnADifferentDay()
    {
        using var bot = new TestHarness();
        bot.SendGroupMessage(ChatId, Alice, "/birthday_add", "Alice");
        bot.TapButton(Alice, "Bob", "Alice");
        bot.TapButton(Alice, "01-JAN-1990", "Alice");

        var chatData = (mod_birthday_data)Chats.getChat(ChatId).getPluginData(typeof(mod_birthday_data));
        // Guard against the test itself running on Jan 1st and coincidentally matching.
        if (DateTime.Now.Month == 1 && DateTime.Now.Day == 1)
        {
            chatData.birthdays.Single(b => b.name == "Bob").birthday = new DateTime(1990, 6, 15);
        }

        bot.RunBackgroundProcessing();

        Assert.DoesNotContain(bot.BotClient.SentMessages, m => m.Text.Contains("Happy Birthday"));
    }

    [Fact]
    public void BackgroundProcessingOnlyAnnouncesOnceEvenIfRunTwiceTheSameDay()
    {
        using var bot = new TestHarness();
        bot.SendGroupMessage(ChatId, Alice, "/birthday_add", "Alice");
        bot.TapButton(Alice, "Bob", "Alice");
        bot.TapButton(Alice, "01-JAN-1990", "Alice");

        var chatData = (mod_birthday_data)Chats.getChat(ChatId).getPluginData(typeof(mod_birthday_data));
        chatData.birthdays.Single(b => b.name == "Bob").birthday = new DateTime(1990, DateTime.Now.Month, DateTime.Now.Day);

        bot.RunBackgroundProcessing();
        bot.RunBackgroundProcessing();

        Assert.Equal(1, bot.BotClient.SentMessages.Count(m => m.Text == "Happy Birthday to Bob!"));
    }
}
