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
    MessageDispatcher dispatcher) : BackgroundService
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
                // CallbackQuery: inline-keyboard button taps - added for mod_xyzzy's card-selection
                // and judging UX (see CallbackQueryRouter).
                AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery]
            };

            await _botClient.ReceiveAsync(
                updateHandler: dispatcher.DispatchAsync,
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

    private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Telegram polling error");
        return Task.CompletedTask;
    }
}
