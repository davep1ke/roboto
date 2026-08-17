using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Roboto.Bot;
using Roboto.Bot.Persistence;
using Roboto.Bot.Xyzzy;

namespace Roboto.Bot.Tests;

public class SqliteStateStoreTests
{
    /// <summary>Regression test for a real incident (2026-08-17): default JsonSerializerOptions
    /// store enums as their raw number, so inserting XyzzyStatus.SettingUp into the middle of that
    /// enum (phase 8.5) silently reinterpreted an already-live game's persisted status after deploy
    /// - "Status": 1 used to mean Invites, then meant SettingUp, with no exception anywhere. Storing
    /// by name means future enum reordering/insertion can never again change what old data means.</summary>
    [Fact]
    public async Task EnumsArePersistedByNameNotOrdinal()
    {
        using var bot = new TestBot();
        var store = bot.Services.GetRequiredService<IStateStore>();
        var dbPath = Path.Combine(bot.Services.GetRequiredService<IOptions<BotOptions>>().Value.InstanceDir, "roboto.db");

        await store.SaveAsync("test:enum-check", new XyzzyGameState { ChatId = 1, Status = XyzzyStatus.SettingUp }, CancellationToken.None);

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM state WHERE key = 'test:enum-check';";
        var json = (string)(await command.ExecuteScalarAsync())!;

        Assert.Contains("\"SettingUp\"", json);
        Assert.DoesNotContain("\"Status\":1", json);
    }

    /// <summary>The converter still accepts a bare number on read, so old numeric-encoded rows (or
    /// any future numeric-encoded data) don't become unreadable - they just keep meaning whatever
    /// ordinal they were written with, which is the actual bug class this incident exposed, not
    /// something readable-at-all-ness can fix retroactively.</summary>
    [Fact]
    public async Task OldNumericEncodedEnumsStillDeserialize()
    {
        using var bot = new TestBot();
        var dbPath = Path.Combine(bot.Services.GetRequiredService<IOptions<BotOptions>>().Value.InstanceDir, "roboto.db");

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO state (key, json, updated_utc) VALUES ('test:legacy-numeric', '{\"ChatId\":1,\"Status\":2}', '2020-01-01T00:00:00Z');";
            await command.ExecuteNonQueryAsync();
        }

        var store = bot.Services.GetRequiredService<IStateStore>();
        var loaded = await store.LoadAsync<XyzzyGameState>("test:legacy-numeric", CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(XyzzyStatus.Invites, loaded!.Status); // ordinal 2 under the *current* enum ordering
    }
}
