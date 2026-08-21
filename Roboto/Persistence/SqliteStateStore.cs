using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace RobotoChatBot.Persistence
{
    /// <summary>
    /// A fresh SqliteConnection per operation, per Microsoft.Data.Sqlite's own recommendation - the
    /// provider pools the underlying native handles, and it sidesteps SqliteConnection not being
    /// thread-safe for concurrent use. Fine at this codebase's scale (single-threaded message loop -
    /// see IStateStore's own comment) - revisit once the phase-4 background scheduler runs
    /// concurrently with it.
    ///
    /// Uses System.Text.Json (built-in, no extra dependency) for the blob table, rather than
    /// Newtonsoft.Json, which the rest of this codebase (still) uses for the legacy XML-adjacent
    /// data shapes and CardCast/Steam API parsing - genuinely unrelated concerns, both stay.
    ///
    /// Real table schemas for the whole-bot-scoped growing collections (expected replies, stats,
    /// chat presence, the xyzzy card/pack catalog) live here too, alongside the generic blob table -
    /// this is the one class that owns the SQLite file, so it owns every table in it. Their actual
    /// read/write accessors are added as their own public methods here (not part of IStateStore,
    /// which is specifically the generic blob-key/value contract) as each is wired up to its
    /// consumer, not designed speculatively up front.
    /// </summary>
    public sealed class SqliteStateStore : IStateStore
    {
        // IncludeFields = true is critical, not cosmetic: this whole codebase's data model classes
        // (chat, mod_xyzzy_card, ExpectedReply, every RobotoModuleDataTemplate/
        // RobotoModuleChatDataTemplate subtype, ...) use public fields throughout, not properties -
        // System.Text.Json only serializes properties by default. Without this, every blob would
        // silently round-trip as an empty JSON object (no exception anywhere - the most dangerous
        // kind of bug), since STJ has nothing else to complain about when there's simply nothing it
        // considers serializable.
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            Converters = { new JsonStringEnumConverter() },
            IncludeFields = true,
        };

        private readonly string _connectionString;

        public SqliteStateStore(string dbPath)
        {
            _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        }

        public void Initialize()
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS state (
                    key TEXT PRIMARY KEY NOT NULL,
                    json TEXT NOT NULL,
                    updated_utc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS expected_replies (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    chat_id INTEGER NOT NULL,
                    user_id INTEGER NOT NULL,
                    user_name TEXT,
                    is_private_message INTEGER NOT NULL,
                    time_logged TEXT NOT NULL,
                    time_sent_to_user TEXT NOT NULL,
                    text TEXT NOT NULL,
                    reply_to_message_id INTEGER NOT NULL,
                    selective INTEGER NOT NULL,
                    keyboard_json TEXT,
                    expects_reply INTEGER NOT NULL,
                    mark_down INTEGER NOT NULL,
                    clear_keyboard INTEGER NOT NULL,
                    message_data TEXT,
                    plugin_type TEXT,
                    outbound_message_id INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_expected_replies_user_id ON expected_replies(user_id);

                CREATE TABLE IF NOT EXISTS stats (
                    stat_name TEXT NOT NULL,
                    module_type TEXT NOT NULL,
                    time_slice TEXT NOT NULL,
                    count INTEGER NOT NULL,
                    PRIMARY KEY (stat_name, module_type, time_slice)
                );

                CREATE TABLE IF NOT EXISTS chat_presence (
                    user_id INTEGER NOT NULL,
                    chat_id INTEGER NOT NULL,
                    user_name TEXT,
                    last_seen TEXT NOT NULL,
                    PRIMARY KEY (user_id, chat_id)
                );
                CREATE INDEX IF NOT EXISTS idx_chat_presence_chat_id ON chat_presence(chat_id);

                CREATE TABLE IF NOT EXISTS xyzzy_cards (
                    unique_id TEXT PRIMARY KEY NOT NULL,
                    card_type TEXT NOT NULL,
                    text TEXT NOT NULL,
                    category TEXT,
                    pack_id TEXT NOT NULL,
                    nr_answers INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_xyzzy_cards_pack_id ON xyzzy_cards(pack_id);
                CREATE INDEX IF NOT EXISTS idx_xyzzy_cards_card_type ON xyzzy_cards(card_type);

                CREATE TABLE IF NOT EXISTS xyzzy_packs (
                    pack_id TEXT PRIMARY KEY NOT NULL,
                    name TEXT,
                    pack_code TEXT,
                    description TEXT,
                    language TEXT,
                    category TEXT,
                    pack_source TEXT,
                    last_picked_date TEXT,
                    total_picks INTEGER NOT NULL,
                    next_sync TEXT,
                    fail_count INTEGER NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        public T Load<T>(string key)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT json FROM state WHERE key = $key;";
            command.Parameters.AddWithValue("$key", key);

            var result = command.ExecuteScalar();
            return result is string json ? JsonSerializer.Deserialize<T>(json, JsonOptions) : default;
        }

        public List<T> LoadAll<T>(string keyPattern)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT json FROM state WHERE key LIKE $pattern;";
            command.Parameters.AddWithValue("$pattern", keyPattern);

            var results = new List<T>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (JsonSerializer.Deserialize<T>(reader.GetString(0), JsonOptions) is { } value)
                {
                    results.Add(value);
                }
            }

            return results;
        }

        public List<string> LoadAllKeys(string keyPattern)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT key FROM state WHERE key LIKE $pattern;";
            command.Parameters.AddWithValue("$pattern", keyPattern);

            var keys = new List<string>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                keys.Add(reader.GetString(0));
            }

            return keys;
        }

        public void Save<T>(string key, T value)
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);

            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO state (key, json, updated_utc) VALUES ($key, $json, $updated)
                ON CONFLICT(key) DO UPDATE SET json = excluded.json, updated_utc = excluded.updated_utc;
                """;
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$json", json);
            command.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));

            command.ExecuteNonQuery();
        }

        public void Delete(string key)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM state WHERE key = $key;";
            command.Parameters.AddWithValue("$key", key);
            command.ExecuteNonQuery();
        }

        public object Load(Type type, string key)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT json FROM state WHERE key = $key;";
            command.Parameters.AddWithValue("$key", key);

            var result = command.ExecuteScalar();
            return result is string json ? JsonSerializer.Deserialize(json, type, JsonOptions) : null;
        }

        public void Save(Type type, string key, object value)
        {
            var json = JsonSerializer.Serialize(value, type, JsonOptions);

            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO state (key, json, updated_utc) VALUES ($key, $json, $updated)
                ON CONFLICT(key) DO UPDATE SET json = excluded.json, updated_utc = excluded.updated_utc;
                """;
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$json", json);
            command.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));

            command.ExecuteNonQuery();
        }

        internal SqliteConnection Open()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return connection;
        }

        // -- expected_replies -------------------------------------------------------------------
        // Kept to the same "load everything into memory at startup, flush everything back at
        // save()" timing model settings.cs's whole graph already used under XmlSerializer (see
        // MIGRATION.md's phase 3 notes on this scoping choice) - a full delete+reinsert per save(),
        // not per-mutation write-through. Real durability of in-flight ExpectedReply state across an
        // unclean shutdown is a real, separately-scoped follow-up, not silently assumed to be solved
        // by "it's a table now".

        public List<ExpectedReply> LoadExpectedReplies()
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT chat_id, user_id, user_name, is_private_message, time_logged, time_sent_to_user,
                       text, reply_to_message_id, selective, keyboard_json, expects_reply, mark_down,
                       clear_keyboard, message_data, plugin_type, outbound_message_id
                FROM expected_replies;
                """;

            var results = new List<ExpectedReply>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new ExpectedReply
                {
                    chatID = reader.GetInt64(0),
                    userID = reader.GetInt64(1),
                    userName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    isPrivateMessage = reader.GetInt64(3) != 0,
                    timeLogged = DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    timeSentToUser = DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    text = reader.GetString(6),
                    replyToMessageID = reader.GetInt64(7),
                    selective = reader.GetInt64(8) != 0,
                    keyboard = reader.IsDBNull(9) ? null : JsonSerializer.Deserialize<List<List<string>>>(reader.GetString(9), JsonOptions),
                    expectsReply = reader.GetInt64(10) != 0,
                    markDown = reader.GetInt64(11) != 0,
                    clearKeyboard = reader.GetInt64(12) != 0,
                    messageData = reader.IsDBNull(13) ? null : reader.GetString(13),
                    pluginType = reader.IsDBNull(14) ? null : reader.GetString(14),
                    outboundMessageID = reader.GetInt64(15),
                });
            }

            return results;
        }

        public void SaveExpectedReplies(List<ExpectedReply> replies)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();

            using (var deleteCommand = connection.CreateCommand())
            {
                deleteCommand.Transaction = transaction;
                deleteCommand.CommandText = "DELETE FROM expected_replies;";
                deleteCommand.ExecuteNonQuery();
            }

            foreach (var er in replies)
            {
                using var insertCommand = connection.CreateCommand();
                insertCommand.Transaction = transaction;
                insertCommand.CommandText =
                    """
                    INSERT INTO expected_replies
                        (chat_id, user_id, user_name, is_private_message, time_logged, time_sent_to_user,
                         text, reply_to_message_id, selective, keyboard_json, expects_reply, mark_down,
                         clear_keyboard, message_data, plugin_type, outbound_message_id)
                    VALUES
                        ($chat_id, $user_id, $user_name, $is_private_message, $time_logged, $time_sent_to_user,
                         $text, $reply_to_message_id, $selective, $keyboard_json, $expects_reply, $mark_down,
                         $clear_keyboard, $message_data, $plugin_type, $outbound_message_id);
                    """;
                insertCommand.Parameters.AddWithValue("$chat_id", er.chatID);
                insertCommand.Parameters.AddWithValue("$user_id", er.userID);
                insertCommand.Parameters.AddWithValue("$user_name", (object)er.userName ?? DBNull.Value);
                insertCommand.Parameters.AddWithValue("$is_private_message", er.isPrivateMessage ? 1 : 0);
                insertCommand.Parameters.AddWithValue("$time_logged", er.timeLogged.ToString("O"));
                insertCommand.Parameters.AddWithValue("$time_sent_to_user", er.timeSentToUser.ToString("O"));
                insertCommand.Parameters.AddWithValue("$text", er.text ?? "");
                insertCommand.Parameters.AddWithValue("$reply_to_message_id", er.replyToMessageID);
                insertCommand.Parameters.AddWithValue("$selective", er.selective ? 1 : 0);
                insertCommand.Parameters.AddWithValue("$keyboard_json", er.keyboard == null ? (object)DBNull.Value : JsonSerializer.Serialize(er.keyboard, JsonOptions));
                insertCommand.Parameters.AddWithValue("$expects_reply", er.expectsReply ? 1 : 0);
                insertCommand.Parameters.AddWithValue("$mark_down", er.markDown ? 1 : 0);
                insertCommand.Parameters.AddWithValue("$clear_keyboard", er.clearKeyboard ? 1 : 0);
                insertCommand.Parameters.AddWithValue("$message_data", (object)er.messageData ?? DBNull.Value);
                insertCommand.Parameters.AddWithValue("$plugin_type", (object)er.pluginType ?? DBNull.Value);
                insertCommand.Parameters.AddWithValue("$outbound_message_id", er.outboundMessageID);
                insertCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        // -- chat_presence ------------------------------------------------------------------------

        public List<chatPresence> LoadChatPresence()
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT user_id, chat_id, user_name, last_seen FROM chat_presence;";

            var results = new List<chatPresence>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new chatPresence(reader.GetInt64(0), reader.GetInt64(1), reader.IsDBNull(2) ? "" : reader.GetString(2))
                {
                    lastSeen = DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
                });
            }

            return results;
        }

        public void SaveChatPresence(List<chatPresence> presence)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();

            using (var deleteCommand = connection.CreateCommand())
            {
                deleteCommand.Transaction = transaction;
                deleteCommand.CommandText = "DELETE FROM chat_presence;";
                deleteCommand.ExecuteNonQuery();
            }

            foreach (var p in presence)
            {
                using var insertCommand = connection.CreateCommand();
                insertCommand.Transaction = transaction;
                insertCommand.CommandText =
                    """
                    INSERT INTO chat_presence (user_id, chat_id, user_name, last_seen)
                    VALUES ($user_id, $chat_id, $user_name, $last_seen);
                    """;
                insertCommand.Parameters.AddWithValue("$user_id", p.userID);
                insertCommand.Parameters.AddWithValue("$chat_id", p.chatID);
                insertCommand.Parameters.AddWithValue("$user_name", (object)p.userName ?? DBNull.Value);
                insertCommand.Parameters.AddWithValue("$last_seen", p.lastSeen.ToString("O"));
                insertCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        // -- stats ------------------------------------------------------------------------------
        // Only stat NAMES/MODULE TYPES/data POINTS persist here - display color/mode (statType's
        // own displayMode/statMode/color fields) are recomputed fresh every startup by each module's
        // own registerStatType(...) calls (see stats.startup()), same as legacy always did; loading
        // just needs to reproduce statsList's existing shape (grouped by name+moduleType, each with
        // its statSlices) closely enough that registerStatType's "already exists -> reuse, just
        // update display settings" branch finds them, exactly like it already did against
        // XmlSerializer-loaded data.

        public List<statType> LoadStats()
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT stat_name, module_type, time_slice, count FROM stats ORDER BY stat_name, module_type;";

            var byKey = new Dictionary<(string, string), statType>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var statName = reader.GetString(0);
                var moduleType = reader.GetString(1);
                var timeSlice = DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind);
                var count = reader.GetInt32(3);

                var key = (statName, moduleType);
                if (!byKey.TryGetValue(key, out var type))
                {
                    type = new statType { name = statName, moduleType = moduleType };
                    byKey[key] = type;
                }

                type.statSlices.Add(new statSlice(timeSlice) { count = count });
            }

            return new List<statType>(byKey.Values);
        }

        public void SaveStats(List<statType> statsList)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();

            using (var deleteCommand = connection.CreateCommand())
            {
                deleteCommand.Transaction = transaction;
                deleteCommand.CommandText = "DELETE FROM stats;";
                deleteCommand.ExecuteNonQuery();
            }

            foreach (var type in statsList)
            {
                foreach (var slice in type.statSlices)
                {
                    using var insertCommand = connection.CreateCommand();
                    insertCommand.Transaction = transaction;
                    insertCommand.CommandText =
                        """
                        INSERT INTO stats (stat_name, module_type, time_slice, count)
                        VALUES ($stat_name, $module_type, $time_slice, $count)
                        ON CONFLICT(stat_name, module_type, time_slice) DO UPDATE SET count = excluded.count;
                        """;
                    insertCommand.Parameters.AddWithValue("$stat_name", type.name);
                    insertCommand.Parameters.AddWithValue("$module_type", type.moduleType);
                    insertCommand.Parameters.AddWithValue("$time_slice", slice.timeSlice.ToString("O"));
                    insertCommand.Parameters.AddWithValue("$count", slice.count);
                    insertCommand.ExecuteNonQuery();
                }
            }

            transaction.Commit();
        }
    }
}
