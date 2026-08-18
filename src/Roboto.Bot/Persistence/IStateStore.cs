namespace Roboto.Bot.Persistence;

/// <summary>
/// JSON-blob-per-aggregate storage, backed by SQLite - the replacement for the legacy app's "one
/// giant XML file, rewritten whole on every save". Callers own their own key scheme and their own
/// POCO shape; there's no schema migration to run when a caller adds a field, same "just add a
/// property" flexibility the old XmlSerializer-based module data had, but with crash-safe,
/// incremental per-key writes instead of a whole-file rewrite.
/// </summary>
public interface IStateStore
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken);

    /// <summary>Loads every value whose key matches a SQL LIKE pattern (caller-supplied, e.g.
    /// "xyzzy:%:game") - added for mod_xyzzy's background scheduler, which needs to find every
    /// active game rather than one known key. Still "callers own their own key scheme": this
    /// doesn't impose any prefix/suffix convention, just lets a caller who already has one query by
    /// it.</summary>
    Task<IReadOnlyList<T>> LoadAllAsync<T>(string keyPattern, CancellationToken cancellationToken);

    /// <summary>Like LoadAllAsync, but returns the matching keys themselves rather than their
    /// deserialized values - for a caller whose value type doesn't carry its own identity (e.g.
    /// DmOutbox's per-user queues are keyed "dm-outbox:{userId}", but DmOutboxEntry itself has no
    /// UserId field - the key is the only place that id lives).</summary>
    Task<IReadOnlyList<string>> LoadAllKeysAsync(string keyPattern, CancellationToken cancellationToken);

    Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken);

    Task DeleteAsync(string key, CancellationToken cancellationToken);
}
