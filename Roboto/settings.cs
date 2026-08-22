using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using RobotoChatBot.Modules;
using RobotoChatBot.Persistence;

namespace RobotoChatBot
{

    /// <summary>
    /// Ported off one big XmlSerializer round-trip of this entire object graph to one file, onto
    /// SqliteStateStore: small/bounded state (this object's own scalar config, each chat's per-
    /// module data, each module's own global core-data) as JSON blob rows; genuinely whole-bot-
    /// scoped growing collections (expectedReplies, stats, RecentChatMembers, and - a real scale
    /// concern, not just "whole bot list" framing - the xyzzy card/pack catalog) as real SQL tables -
    /// see SqliteStateStore's own comment for why the split, and MIGRATION.md's phase 3 notes for
    /// what's still deliberately deferred (real per-mutation write-through durability for
    /// expectedReplies/stats/etc is a separate follow-up, not delivered by this pass - every table
    /// still only flushes at save(), same timing model XmlSerializer always had).
    ///
    /// The fields below marked [JsonIgnore] are populated by load()/save() from elsewhere (their own
    /// blob rows or real tables), not as part of this object's own blob - keeping them as real
    /// fields on `settings` (rather than a separate DTO) means every existing Roboto.Settings.X call
    /// site across the whole codebase keeps working unchanged; only load()/save() themselves needed
    /// to change.
    /// </summary>
    public class settings
    {
        //logging
        public bool enableFileLogging = true;
        public int rotateLogsEveryXHours = 12;
        public int saveXMLeveryXMins = 30;
        public int killInactiveChatsAfterXDays = 30;
        public int purgeInactiveChatsAfterXDays = 100;
        public int chatPresenceExpiresAfterHours = 96;


        //stats database - statsList itself is loaded/saved via the real `stats` table
        //(SqliteStateStore.LoadStats/SaveStats), not part of this blob.
        [JsonIgnore]
        public stats stats = new stats();

        //Credentials come from InstanceBootstrapper (bot.env), via Roboto.Options - not persisted
        //in this blob at all, so there's only ever one place a token can go stale.
        [JsonIgnore]
        public string telegramAPIURL = "https://api.telegram.org/bot";
        [JsonIgnore]
        public string telegramAPIKey = "ENTERYOURAPIKEYHERE";
        [JsonIgnore]
        public string botUserName = "Roboto_bot_name";
        public int waitDuration = 60; //wait duration for long polling.
        public int lastUpdate = 0; //last update index, needs to be passed back with each call.
        public int maxLogItems = 50;

        //generic plugin storage - loaded/saved as one blob row per (module type)/(chat, module type),
        //not part of this blob itself. NB: Chats DO want to be persisted.
        [JsonIgnore]
        public List<Modules.RobotoModuleDataTemplate> pluginData = new List<Modules.RobotoModuleDataTemplate>();
        [JsonIgnore]
        public List<chat> chatData = new List<chat>();

        //Random generator
        static Random randGen = new Random();

        //list of expected replies - loaded/saved via the real `expected_replies` table, not part of
        //this blob.
        [JsonIgnore]
        public List<ExpectedReply> expectedReplies = new List<ExpectedReply>();
        //loaded/saved via the real `chat_presence` table, not part of this blob.
        [JsonIgnore]
        public List<chatPresence> RecentChatMembers = new List<chatPresence>();

        //is this the first time the settings file has been initialised? - transient, not persisted.
        [JsonIgnore]
        public bool isFirstTimeInitialised = false;

        private const string ConfigKey = "settings:config";
        private const string ChatsIndexKey = "chats:index";


        /// <summary>
        /// Load all our data from SQLite (was XML) - Roboto.Store must already be constructed and
        /// initialised, and Plugins.initPluginAssemblies() must already have run (this needs to
        /// enumerate Plugins.plugins to know which per-module/per-chat blob keys to look for, the
        /// same ordering requirement XmlSerializer's extraTypes had).
        /// </summary>
        /// <returns></returns>
        public static settings load()
        {
            Roboto.log.log("Loading settings from " + Roboto.Options.InstanceDir, logging.loglevel.high);

            var store = Roboto.Store;
            var setts = store.Load<settings>(ConfigKey);
            if (setts == null)
            {
                setts = new settings();
                setts.isFirstTimeInitialised = true;
            }

            setts.telegramAPIKey = Roboto.Options.TelegramToken;
            setts.botUserName = Roboto.Options.BotUsername;

            //module-global data - one blob row per registered module type.
            foreach (var plugin in Plugins.plugins.Where(p => p.pluginDataType != null))
            {
                if (store.Load(plugin.pluginDataType, ModuleDataKey(plugin.pluginDataType)) is Modules.RobotoModuleDataTemplate data)
                {
                    setts.pluginData.Add(data);
                }
            }

            //xyzzy's card/pack catalog - real tables (see mod_xyzzy_coredata's own [JsonIgnore]
            //comment), not part of that module's blob row above. A fresh instance (no rows in
            //xyzzy_packs yet) keeps whichever mod_xyzzy_coredata.pack/questions/answers field
            //initializers it already got (the 7 default CAH packs, empty catalog) - same fallback
            //convention as any other module's fresh-instance case.
            if (setts.pluginData.OfType<mod_xyzzy_coredata>().FirstOrDefault() is { } xyzzyData)
            {
                var loadedQuestions = store.LoadXyzzyCards("question");
                var loadedAnswers = store.LoadXyzzyCards("answer");
                var loadedPacks = store.LoadXyzzyPacks();
                if (loadedPacks.Count > 0)
                {
                    xyzzyData.questions = loadedQuestions;
                    xyzzyData.answers = loadedAnswers;
                    xyzzyData.packs = loadedPacks;
                }
            }

            //chats - one blob row per chat for its own scalars, plus one blob row per (chat, module
            //type) for that chat's module data, reassembled into chat.chatData same as it always was
            //in memory.
            var chatIds = store.Load<List<long>>(ChatsIndexKey) ?? new List<long>();
            foreach (var chatId in chatIds)
            {
                var c = store.Load<chat>(ChatCoreKey(chatId));
                if (c == null) { continue; }

                // chat's only public constructor - the one STJ's reflection-based converter picks
                // automatically, being the sole public parameterized ctor - calls initPlugins(),
                // which stub-populates c.chatData with one fresh entry per registered module before
                // this loop below ever runs (chatData starts empty at that point, so initPlugins()'s
                // own "do we already have this module's data?" check finds nothing and adds a stub
                // for every one of them, real data or not). Real bug, found via phase 8's migrator
                // tests (no existing test had exercised a full save()-then-reload round trip with
                // real per-chat module data before): appending the loaded row instead of replacing
                // the stub left both in chatData, stub first - and getPluginData<T>()/getPluginData()
                // both return the *first* match, so every module lookup after any restart silently
                // got the fresh stub instead of the real persisted state, for that entire run.
                // RemoveAll first so the real row actually replaces the stub; a module with no saved
                // row yet (freshly added, or a chat that's never touched it) correctly keeps its stub.
                foreach (var plugin in Plugins.plugins.Where(p => p.pluginChatDataType != null))
                {
                    if (store.Load(plugin.pluginChatDataType, ChatModuleKey(chatId, plugin.pluginChatDataType)) is Modules.RobotoModuleChatDataTemplate cd)
                    {
                        c.chatData.RemoveAll(existing => existing.GetType() == plugin.pluginChatDataType);
                        c.chatData.Add(cd);
                    }
                }

                setts.chatData.Add(c);
            }

            setts.expectedReplies = store.LoadExpectedReplies();
            setts.RecentChatMembers = store.LoadChatPresence();
            setts.stats.statsList = store.LoadStats();

            return setts;
        }


        /// <summary>
        /// Save all data to SQLite (was XML).
        /// </summary>
        public void save()
        {
            //as we are saving (and presumably exiting) we dont need to worry that this is a first time file anymore
            isFirstTimeInitialised = false;

            var store = Roboto.Store;

            store.Save(ConfigKey, this);

            // Snapshot the shared top-level lists under GlobalListsKey (see ChatKeyedLock's own
            // comment - this is the same phase-4 concurrency concern as everywhere else in this
            // pass), then do the actual (many small, potentially slow) DB writes below outside that
            // lock so a save() running on the background scheduler thread can't block live message
            // dispatch on unrelated chats for its whole duration. Each chat's own data is still
            // locked individually (by that chat's own ID) while being written, the same chokepoint
            // live dispatch for that chat locks against.
            List<Modules.RobotoModuleDataTemplate> pluginDataSnapshot;
            List<chat> chatDataSnapshot;
            List<ExpectedReply> expectedRepliesSnapshot;
            List<chatPresence> recentChatMembersSnapshot;
            List<statType> statsListSnapshot;
            using (ChatKeyedLock.Acquire(ChatKeyedLock.GlobalListsKey))
            {
                pluginDataSnapshot = pluginData.ToList();
                chatDataSnapshot = chatData.ToList();
                expectedRepliesSnapshot = expectedReplies.ToList();
                recentChatMembersSnapshot = RecentChatMembers.ToList();
                statsListSnapshot = stats.statsList.ToList();
            }

            foreach (var data in pluginDataSnapshot)
            {
                store.Save(data.GetType(), ModuleDataKey(data.GetType()), data);
            }

            if (pluginDataSnapshot.OfType<mod_xyzzy_coredata>().FirstOrDefault() is { } xyzzyData)
            {
                store.SaveXyzzyCards("question", xyzzyData.questions);
                store.SaveXyzzyCards("answer", xyzzyData.answers);
                store.SaveXyzzyPacks(xyzzyData.packs);
            }

            foreach (var c in chatDataSnapshot)
            {
                using (ChatKeyedLock.Acquire(c.chatID))
                {
                    store.Save(ChatCoreKey(c.chatID), c);
                    foreach (var cd in c.chatData)
                    {
                        store.Save(cd.GetType(), ChatModuleKey(c.chatID, cd.GetType()), cd);
                    }
                }
            }
            store.Save(ChatsIndexKey, chatDataSnapshot.Select(c => c.chatID).ToList());

            // These three writes touch each ExpectedReply/chatPresence/statType's own fields (not
            // just the outer list) while iterating - held under GlobalListsKey for their whole
            // duration, unlike everything else in this method, since a shallow .ToList() snapshot of
            // the outer list alone doesn't protect e.g. a statType's own nested statSlices list from
            // a concurrent logStat() call elsewhere. Acceptable here specifically because these are
            // fast local SQLite writes, not network calls - a bounded, brief hold, not the "block
            // everything for an arbitrarily long call" case this pass otherwise avoids.
            using (ChatKeyedLock.Acquire(ChatKeyedLock.GlobalListsKey))
            {
                store.SaveExpectedReplies(expectedRepliesSnapshot);
                store.SaveChatPresence(recentChatMembersSnapshot);
                store.SaveStats(statsListSnapshot);
            }
        }

        /// <summary>
        /// Phase 8: parses a legacy XML export (the exact XmlSerializer(typeof(settings), extraTypes)
        /// shape production ran on before phase 3 replaced it with SqliteStateStore) directly into
        /// this branch's own live settings/chat/module-data types - the plan file's own predicted
        /// shape ("the importer can most likely deserialize straight into the real live types...
        /// eliminating the shadow-class layer the abandoned rewrite branch needed"), since this
        /// branch's classes are still legacy's own classes, not a redesigned shape. Requires
        /// Plugins.plugins already populated (Plugins.initPluginAssemblies()) - same ordering
        /// requirement load()/save() have, since XmlSerializer needs every module's
        /// pluginDataType/pluginChatDataType as its extraTypes to deserialize the polymorphic
        /// pluginData/chatData lists into their real concrete subtypes. Read-only against the source
        /// file - never opens it for anything but reading.
        ///
        /// Real legacy XML persisted the live Telegram token in telegramAPIKey (that field's
        /// "ENTERYOURAPIKEYHERE" default is the unconfigured placeholder) - scrubbed back to defaults
        /// immediately after deserializing, on top of save() already never persisting these three
        /// fields (JsonIgnore'd from the SQLite blob). Credentials only ever come from
        /// InstanceBootstrapper's bot.env; this import path must never become a second route for one
        /// to travel through, even transiently.
        /// </summary>
        public static settings loadFromLegacyXml(string xmlPath)
        {
            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(settings), Plugins.getPluginDataTypes());
            using var reader = new System.IO.StreamReader(xmlPath);
            var imported = (settings)serializer.Deserialize(reader);

            imported.isFirstTimeInitialised = false;
            imported.telegramAPIKey = "ENTERYOURAPIKEYHERE";
            imported.telegramAPIURL = "https://api.telegram.org/bot";
            imported.botUserName = "Roboto_bot_name";

            return imported;
        }

        private static string ModuleDataKey(Type moduleDataType) => $"module:{moduleDataType.Name}";
        private static string ChatCoreKey(long chatId) => $"chat:{chatId}";
        private static string ChatModuleKey(long chatId, Type moduleChatDataType) => $"chat:{chatId}:{moduleChatDataType.Name}";


        public static int getRandom(int maxInt)
        {
            return randGen.Next(maxInt);
        }


    }

}
