using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Roboto.Bot;
using Roboto.Bot.Birthdays;
using Roboto.Bot.Chats;
using Roboto.Bot.Commands;
using Roboto.Bot.Persistence;
using Roboto.Bot.Quotes;
using Roboto.Bot.Steam;
using Roboto.Bot.Xyzzy;
using Serilog;
using Telegram.Bot;

var builder = Host.CreateApplicationBuilder(args);

// Only Instance and DataDir come from real env vars - e.g. `docker run -e ROBOTO_INSTANCE=beefy`.
// TelegramToken/BotUsername come from that instance's own bot.env file, not the environment.
builder.Configuration.AddEnvironmentVariables(prefix: "ROBOTO_");
builder.Services.Configure<BotOptions>(builder.Configuration);

var instance = builder.Configuration["Instance"] ?? "default";
var dataDir = builder.Configuration["DataDir"] ?? "/data";

if (!InstanceBootstrapper.TryLoad(dataDir, instance, out var telegramToken, out var botUsername, out var steamApiKey, out var message))
{
    Console.Error.WriteLine(message);
    return 1;
}

builder.Services.Configure<BotOptions>(o =>
{
    o.Instance = instance;
    o.DataDir = dataDir;
    o.TelegramToken = telegramToken;
    o.BotUsername = botUsername;
    o.SteamApiKey = steamApiKey;
});

builder.Services.AddSerilog((_, loggerConfig) => loggerConfig
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

builder.Services.AddRobotoBot();
builder.Services.AddHostedService<TelegramPollingService>();
builder.Services.AddHostedService<XyzzyRoundSchedulerService>();
builder.Services.AddHostedService<BirthdaysSchedulerService>();
builder.Services.AddHostedService<QuotesSchedulerService>();
builder.Services.AddHostedService<SteamSchedulerService>();
builder.Services.AddHostedService<ChatPurgeSchedulerService>();

using var host = builder.Build();

var stateStore = host.Services.GetRequiredService<IStateStore>();
await stateStore.InitializeAsync(CancellationToken.None);

// Swaps in a real, imported card catalog if this instance has one (phase 11's XmlImporter) -
// before any hosted service can touch a game. Every instance that's never had one imported keeps
// the hardcoded placeholder set untouched.
await CardCatalog.LoadOverrideAsync(stateStore, CancellationToken.None);

// Startup safety net - delivers anything left sitting undelivered in a user's DM queue (a prior
// crash mid-pump, or a freshly imported instance's resumed in-flight games/replies - see
// DmOutbox.PumpAllOutstandingAsync's own doc comment). Uses its own TelegramBotClient rather than
// resolving one via DI, same reasoning as every scheduler service - no command/router anywhere
// takes ITelegramBotClient as a constructor dependency either.
await host.Services.GetRequiredService<DmOutbox>()
    .PumpAllOutstandingAsync(new TelegramBotClient(telegramToken), CancellationToken.None);

await host.RunAsync();

// A fatal error inside TelegramPollingService sets this before requesting shutdown; propagate it as
// the real process exit code (an explicit `return` here would otherwise silently override it).
return Environment.ExitCode;
