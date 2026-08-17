using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Commands;

namespace Roboto.Bot.Tests;

/// <summary>
/// Cheapest, highest-value test in this project: would have caught the real circular-DI bug
/// (HelpCommand/CommandRouter, later SetQuietHoursCommand/ReplyRouter) immediately, before ever
/// touching Telegram - resolving the DI graph is enough to surface a cycle.
/// </summary>
public class DiRegistrationTests
{
    [Fact]
    public void AllRegisteredCommandsResolveWithoutCircularDependencyErrors()
    {
        using var bot = new TestBot();

        var router = bot.Services.GetRequiredService<CommandRouter>();
        var replyRouter = bot.Services.GetRequiredService<ReplyRouter>();
        var dispatcher = bot.Services.GetRequiredService<MessageDispatcher>();

        Assert.NotEmpty(router.Commands);
        Assert.NotNull(replyRouter);
        Assert.NotNull(dispatcher);
    }
}
