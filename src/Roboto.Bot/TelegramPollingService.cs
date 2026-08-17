using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot;

public sealed class TelegramPollingService(
    ILogger<TelegramPollingService> logger,
    IOptions<BotOptions> options,
    IHostApplicationLifetime lifetime,
    CommandRouter commandRouter) : BackgroundService
{
    private readonly BotOptions _options = options.Value;
    private TelegramBotClient _botClient = null!;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _botClient = new TelegramBotClient(_options.TelegramToken);

            var me = await _botClient.GetMe(stoppingToken);
            logger.LogInformation("I am @{Username} ({Id})", me.Username, me.Id);

            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = [UpdateType.Message]
            };

            await _botClient.ReceiveAsync(
                updateHandler: HandleUpdateAsync,
                errorHandler: HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown (Ctrl+C / SIGTERM) - not a failure.
        }
        catch (Exception ex)
        {
            // BackgroundServiceExceptionBehavior.StopHost (the default) stops the host cleanly on an
            // unhandled exception here, but doesn't set a non-zero process exit code on its own - do
            // that explicitly so `docker run`/restart policies can tell a crash from a clean exit.
            logger.LogCritical(ex, "Fatal error in Telegram polling loop - stopping");
            Environment.ExitCode = 1;
            lifetime.StopApplication();
        }
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Message is not { Text: { } text } message)
        {
            return;
        }

        logger.LogInformation("Message from {User}: {Text}", message.From?.Username ?? message.From?.FirstName, text);

        await commandRouter.TryDispatchAsync(botClient, message, cancellationToken);
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Telegram polling error");
        return Task.CompletedTask;
    }
}
