using System.Text.Json;
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
/// </summary>
public sealed class SqliteStateStore : IStateStore
{
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
        return result is string json ? JsonSerializer.Deserialize<T>(json) : default;
    }

    public async Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value);

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

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
