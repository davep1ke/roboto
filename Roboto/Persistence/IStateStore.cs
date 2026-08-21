using System;
using System.Collections.Generic;

namespace RobotoChatBot.Persistence
{
    /// <summary>
    /// JSON-blob-per-key storage, backed by SQLite - the replacement for XmlSerializer round-
    /// tripping the whole `settings` object graph to one file. Callers own their own key scheme and
    /// their own POCO shape; adding a field to a stored type needs no schema migration, same "just
    /// add a property" flexibility XmlSerializer gave module data, but with independent per-key
    /// writes instead of a whole-file rewrite.
    ///
    /// Synchronous by design, not adapted async - this whole codebase is single-threaded/blocking
    /// throughout (the message-poll loop, module dispatch, XmlSerializer save/load), and
    /// Microsoft.Data.Sqlite's sync API (ExecuteNonQuery/ExecuteScalar/ExecuteReader) needs no
    /// async wrapper to use correctly. Sprinkling .GetAwaiter().GetResult() at every one of the many
    /// call sites this has (settings.cs, chat.cs, Plugins.cs, every module) would be worse than just
    /// not introducing async in the first place.
    ///
    /// Not everything goes through this store - genuinely whole-bot-scoped growing collections
    /// (expected replies, stats, chat presence, the xyzzy card/pack catalog) are real SQL tables
    /// instead (see Persistence/SqliteStateStore.cs's own table-creation SQL) - this store is for
    /// the small, bounded, always-read/written-as-one-unit state: a chat's per-module data, a
    /// module's own global core-data, top-level settings config knobs.
    /// </summary>
    public interface IStateStore
    {
        void Initialize();

        T Load<T>(string key);

        /// <summary>Loads every value whose key matches a SQL LIKE pattern (e.g. "chat:%:mod_xyzzy")
        /// - callers own their own key scheme, this doesn't impose one.</summary>
        List<T> LoadAll<T>(string keyPattern);

        /// <summary>Like LoadAll, but returns the matching keys themselves - for extracting an
        /// identifier (e.g. a chat ID) that only lives in the key, not the stored value.</summary>
        List<string> LoadAllKeys(string keyPattern);

        void Save<T>(string key, T value);

        void Delete(string key);

        /// <summary>Runtime-Type-parameterized twins of Load&lt;T&gt;/Save&lt;T&gt; - needed for
        /// module data (RobotoModuleDataTemplate/RobotoModuleChatDataTemplate subtypes), where the
        /// concrete type is only known at runtime via a plugin's own pluginDataType/
        /// pluginChatDataType, the same reflection-driven pattern Plugins.cs already uses
        /// (Activator.CreateInstance(pluginDataType)) rather than a generic type argument known at
        /// compile time. Each blob row still holds exactly one concrete type - this is what lets the
        /// blob table sidestep needing any polymorphic-list handling at all.</summary>
        object Load(Type type, string key);

        void Save(Type type, string key, object value);
    }
}
