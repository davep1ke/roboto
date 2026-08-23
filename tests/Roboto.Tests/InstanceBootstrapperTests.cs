using System.IO;
using RobotoChatBot;

namespace RobotoTests;

/// <summary>
/// bot.env's Plugins= line - the persistent, per-instance home for the module allow-list (replacing
/// a docker-compose/launch-command -plugin argument, which would have to be remembered/maintained
/// separately from every other per-instance setting). See CLAUDE.md/MIGRATION.md for why this lives
/// here rather than purely as a CLI flag.
/// </summary>
public class InstanceBootstrapperTests
{
    private static string NewTempDataDir() => Path.Combine(Path.GetTempPath(), $"roboto-bootstrap-test-{System.Guid.NewGuid():N}");

    [Fact]
    public void FirstRunStubIncludesABlankPluginsLineAndReturnsAnEmptyList()
    {
        var dataDir = NewTempDataDir();

        var loaded = InstanceBootstrapper.TryLoad(dataDir, "newinstance", out _, out _, out _, out var plugins, out var message);

        Assert.False(loaded);
        Assert.Empty(plugins);
        Assert.Contains("Plugins=", File.ReadAllText(Path.Combine(dataDir, "newinstance", "bot.env")));
        Directory.Delete(dataDir, true);
    }

    [Fact]
    public void ConfiguredPluginsLineParsesIntoATrimmedList()
    {
        var dataDir = NewTempDataDir();
        var instanceDir = Path.Combine(dataDir, "chat_mangler_bot");
        Directory.CreateDirectory(instanceDir);
        File.WriteAllText(Path.Combine(instanceDir, "bot.env"),
            "TelegramToken=test-token\nBotUsername=TestBot\nPlugins= mod_wordcraft,mod_standard , mod_quote,mod_birthday\n");

        var loaded = InstanceBootstrapper.TryLoad(dataDir, "chat_mangler_bot", out _, out _, out _, out var plugins, out _);

        Assert.True(loaded);
        Assert.Equal(new[] { "mod_wordcraft", "mod_standard", "mod_quote", "mod_birthday" }, plugins);
        Directory.Delete(dataDir, true);
    }

    [Fact]
    public void BlankPluginsLineLoadsEveryModule()
    {
        var dataDir = NewTempDataDir();
        var instanceDir = Path.Combine(dataDir, "robotolive");
        Directory.CreateDirectory(instanceDir);
        File.WriteAllText(Path.Combine(instanceDir, "bot.env"),
            "TelegramToken=test-token\nBotUsername=TestBot\nPlugins=\n");

        var loaded = InstanceBootstrapper.TryLoad(dataDir, "robotolive", out _, out _, out _, out var plugins, out _);

        Assert.True(loaded);
        Assert.Empty(plugins);
        Directory.Delete(dataDir, true);
    }
}
