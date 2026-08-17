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

    Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken);

    Task DeleteAsync(string key, CancellationToken cancellationToken);
}
