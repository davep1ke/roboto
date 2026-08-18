using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Roboto.Bot.Persistence;

/// <summary>
/// A fresh SqliteConnection per operation, per Microsoft.Data.Sqlite's own recommendation - the
/// provider pools the underlying native handles, and it sidesteps SqliteConnection not being
/// thread-safe for concurrent use. Fine at this scale (one bot processing one update at a time) -
/// revisit if Telegram update handling ever stops being sequential.
///
/// Uses System.Text.Json (built-in, no extra dependency) rather than Newtonsoft.Json, which the
/// legacy app uses throughout (Roboto/Newtonsoft.Json.dll, checked in directly). Nothing here
/// needs Newtonsoft's extra features. Not necessarily the right call for code that has to *read*
/// legacy-shaped JSON later (e.g. an XML/JSON migration importer) - that's still open.
///
/// BUG THAT ACTUALLY HAPPENED (2026-08-17): the default JsonSerializerOptions serialize enums as
/// their raw underlying number, not their name. Adding XyzzyStatus.SettingUp into the middle of
/// that enum (phase 8.5) shifted every later value's ordinal by one - a live game already persisted
/// as "Status": 1 (meaning Invites under the old ordering) silently became "SettingUp" after
/// deploying that change, with no exception anywhere: the game looked stuck ("thinks it's asked
/// setup, nothing's actually waiting") purely because a number that used to mean one thing now means
/// another. JsonStringEnumConverter below makes every enum serialize by name instead, so future enum
/// reordering/insertion can't silently reinterpret already-persisted data this way again. Existing
/// numeric-encoded rows written before this fix are unaffected by it (the converter still accepts a
/// bare number on read) - the specific corrupted row from this incident was reset by hand, not
/// migrated, since there was no real round in progress to recover.
/// </summary>
public sealed class SqliteStateStore : IStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _connectionString;

    public SqliteStateStore(IOptions<BotOptions> options)
    {
        var dbPath = Path.Combine(options.Value.InstanceDir, "roboto.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS state (
                key TEXT PRIMARY KEY NOT NULL,
                json TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM state WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is string json ? JsonSerializer.Deserialize<T>(json, JsonOptions) : default;
    }

    public async Task<IReadOnlyList<T>> LoadAllAsync<T>(string keyPattern, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM state WHERE key LIKE $pattern;";
        command.Parameters.AddWithValue("$pattern", keyPattern);

        var results = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (JsonSerializer.Deserialize<T>(reader.GetString(0), JsonOptions) is { } value)
            {
                results.Add(value);
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<string>> LoadAllKeysAsync(string keyPattern, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT key FROM state WHERE key LIKE $pattern;";
        command.Parameters.AddWithValue("$pattern", keyPattern);

        var keys = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            keys.Add(reader.GetString(0));
        }

        return keys;
    }

    public async Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO state (key, json, updated_utc) VALUES ($key, $json, $updated)
            ON CONFLICT(key) DO UPDATE SET json = excluded.json, updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$json", json);
        command.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM state WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
