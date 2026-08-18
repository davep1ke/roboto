using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Birthdays;
using Roboto.Bot.Chats;
using Roboto.Bot.Commands;
using Roboto.Bot.Persistence;
using Roboto.Bot.Quotes;
using Roboto.Bot.Stats;
using Roboto.Bot.Wordcraft;
using Roboto.Bot.Xyzzy;

namespace Roboto.Bot;

/// <summary>
/// Everything the bot needs except BotOptions (caller-specific: env-sourced in Program.cs, fixed
/// values in tests) and logging providers (same reasoning), and except the actual
/// TelegramPollingService background service - tests drive MessageDispatcher directly against a
/// fake ITelegramBotClient instead of running the real long-poll loop. Shared between Program.cs
/// and tests/Roboto.Bot.Tests so tests exercise the exact same service graph as production, not a
/// hand-maintained approximation of it that can quietly drift out of sync.
/// </summary>
public static class RobotoServiceCollectionExtensions
{
    public static IServiceCollection AddRobotoBot(this IServiceCollection services)
    {
        // Reflection-based discovery, same idea the legacy module scan used - just for commands
        // instead of the whole module. Harmless: everything's still compiled into this one
        // assembly, this only saves having to hand-register each new IBotCommand here as they're
        // added.
        foreach (var commandType in typeof(IBotCommand).Assembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IBotCommand).IsAssignableFrom(t)))
        {
            services.AddSingleton(typeof(IBotCommand), commandType);
        }

        // Same reflection-discovery pattern, for inline-keyboard button handlers.
        foreach (var callbackType in typeof(ICallbackQueryHandler).Assembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ICallbackQueryHandler).IsAssignableFrom(t)))
        {
            services.AddSingleton(typeof(ICallbackQueryHandler), callbackType);
        }

        services.AddSingleton<IStateStore, SqliteStateStore>();
        services.AddSingleton<StatsRecorder>();
        services.AddSingleton<ChatRepository>();
        services.AddSingleton<XyzzyGameRepository>();
        services.AddSingleton<XyzzyRoundService>();
        services.AddSingleton<XyzzyRoundReconciler>();
        services.AddSingleton<QuietHoursQuery>();
        services.AddSingleton<WordcraftStore>();
        services.AddSingleton<BirthdaysRepository>();
        services.AddSingleton<BirthdaysReconciler>();
        services.AddSingleton<QuotesRepository>();
        services.AddSingleton<QuotesReconciler>();
        services.AddSingleton(new AppClock(DateTime.UtcNow));
        services.AddSingleton<CommandRouter>();
        services.AddSingleton<DmOutbox>();
        services.AddSingleton<ReplyRouter>();
        services.AddSingleton<CallbackQueryRouter>();
        services.AddSingleton<MessageDispatcher>();

        return services;
    }
}
