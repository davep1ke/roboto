using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Roboto.Bot;
using Roboto.Bot.Persistence;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Only Instance and DataDir come from real env vars - e.g. `docker run -e ROBOTO_INSTANCE=beefy`.
// TelegramToken/BotUsername come from that instance's own bot.env file, not the environment.
builder.Configuration.AddEnvironmentVariables(prefix: "ROBOTO_");
builder.Services.Configure<BotOptions>(builder.Configuration);

var instance = builder.Configuration["Instance"] ?? "default";
var dataDir = builder.Configuration["DataDir"] ?? "/data";

if (!InstanceBootstrapper.TryLoad(dataDir, instance, out var telegramToken, out var botUsername, out var message))
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
});

builder.Services.AddSerilog((_, loggerConfig) => loggerConfig
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

builder.Services.AddRobotoBot();
builder.Services.AddHostedService<TelegramPollingService>();

using var host = builder.Build();

await host.Services.GetRequiredService<IStateStore>().InitializeAsync(CancellationToken.None);

await host.RunAsync();

// A fatal error inside TelegramPollingService sets this before requesting shutdown; propagate it as
// the real process exit code (an explicit `return` here would otherwise silently override it).
return Environment.ExitCode;
