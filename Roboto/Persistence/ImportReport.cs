using System.Collections.Generic;
using System.Linq;
using System.Text;
using RobotoChatBot.Modules;

namespace RobotoChatBot.Persistence
{
    /// <summary>
    /// Phase 8: counts/checksums for validating a legacy XML import - CLAUDE.md's own "validate with
    /// counts/checksums (chat count, player count, expected-reply count, etc.) rather than eyeballing
    /// it" requirement. Built from a settings object, so the same code produces the "what did we read
    /// from the XML" report and the "what's actually in the target store after save()+reload" report -
    /// Diff() is what actually proves round-trip fidelity, not either report alone.
    /// </summary>
    public sealed class ImportReport
    {
        public int ChatCount;
        public int PluginDataModuleCount;
        public int ExpectedReplyCount;
        public int RecentChatMemberCount;
        public int StatTypeCount;
        public int StatSliceCount;
        /// <summary>Registered stat types with no recorded slices - the `stats` table only stores
        /// per-slice rows (SqliteStateStore.SaveStats/LoadStats), so a type with zero data has no row
        /// to persist at all and doesn't survive a save()+reload. Not data loss: each module
        /// re-registers its own stat types fresh on every startup regardless of persistence (see
        /// mod_xyzzy/etc.'s own init/startup calls), so an empty type just reappears from code next
        /// boot - there was never any slice data in it to lose. Tracked separately so the "real" count
        /// (StatTypeCount, types that actually carry data) round-trips exactly instead of reporting a
        /// false mismatch on every real import.</summary>
        public int StatTypesWithNoData;
        public int XyzzyQuestionCount;
        public int XyzzyAnswerCount;
        public int XyzzyPackCount;
        public int XyzzyTotalPlayersAcrossChats;
        public int QuoteCount;
        public int BirthdayCount;

        /// <summary>Chat count carrying each module's chat-data - e.g. how many chats have real
        /// mod_xyzzy_chatdata vs. just a stub. Keyed by the chat-data type's own name.</summary>
        public Dictionary<string, int> ChatsWithModuleData = new();

        public static ImportReport From(settings s)
        {
            var report = new ImportReport
            {
                ChatCount = s.chatData.Count,
                PluginDataModuleCount = s.pluginData.Count,
                ExpectedReplyCount = s.expectedReplies.Count,
                RecentChatMemberCount = s.RecentChatMembers.Count,
                StatTypeCount = s.stats.statsList.Count(t => (t.statSlices?.Count ?? 0) > 0),
                StatTypesWithNoData = s.stats.statsList.Count(t => (t.statSlices?.Count ?? 0) == 0),
                StatSliceCount = s.stats.statsList.Sum(t => t.statSlices?.Count ?? 0),
            };

            foreach (var c in s.chatData)
            {
                foreach (var cd in c.chatData)
                {
                    var key = cd.GetType().Name;
                    report.ChatsWithModuleData[key] = report.ChatsWithModuleData.GetValueOrDefault(key) + 1;
                }
            }

            if (s.pluginData.OfType<mod_xyzzy_coredata>().FirstOrDefault() is { } xyzzy)
            {
                report.XyzzyQuestionCount = xyzzy.questions.Count;
                report.XyzzyAnswerCount = xyzzy.answers.Count;
                report.XyzzyPackCount = xyzzy.packs.Count;
            }
            report.XyzzyTotalPlayersAcrossChats = s.chatData
                .SelectMany(c => c.chatData.OfType<mod_xyzzy_chatdata>())
                .Sum(cd => cd.players.Count);

            report.QuoteCount = s.chatData
                .SelectMany(c => c.chatData.OfType<mod_quote_data>())
                .Sum(cd => cd.quotes.Count + cd.multiquotes.Count);
            report.BirthdayCount = s.chatData
                .SelectMany(c => c.chatData.OfType<mod_birthday_data>())
                .Sum(cd => cd.birthdays.Count);

            return report;
        }

        public string Format(string title)
        {
            var sb = new StringBuilder();
            sb.AppendLine(title);
            sb.AppendLine($"  Chats: {ChatCount}");
            sb.AppendLine($"  Plugin data modules: {PluginDataModuleCount}");
            sb.AppendLine($"  Expected replies: {ExpectedReplyCount}");
            sb.AppendLine($"  Recent chat members: {RecentChatMemberCount}");
            sb.AppendLine($"  Stat types with data: {StatTypeCount} ({StatSliceCount} total slices)"
                + (StatTypesWithNoData > 0 ? $" - plus {StatTypesWithNoData} registered but empty (expected to not survive the round trip; re-registered fresh by module code on next boot)" : ""));
            sb.AppendLine($"  Xyzzy catalog: {XyzzyQuestionCount} questions, {XyzzyAnswerCount} answers, {XyzzyPackCount} packs");
            sb.AppendLine($"  Xyzzy players (across all chats' current games): {XyzzyTotalPlayersAcrossChats}");
            sb.AppendLine($"  Quotes: {QuoteCount}");
            sb.AppendLine($"  Birthdays: {BirthdayCount}");
            sb.AppendLine("  Chats with module data:");
            foreach (var (module, count) in ChatsWithModuleData.OrderBy(kv => kv.Key))
            {
                sb.AppendLine($"    {module}: {count}");
            }
            return sb.ToString();
        }

        /// <summary>Every mismatch between two reports, empty if identical. Used to compare
        /// "what the XML said" against "what's actually in the target store after save()+reload" -
        /// any non-empty result means the round trip lost or changed something and the import should
        /// not be trusted.</summary>
        public static List<string> Diff(ImportReport before, ImportReport after)
        {
            var diffs = new List<string>();
            void Check(string name, int a, int b)
            {
                if (a != b) diffs.Add($"{name}: {a} -> {b}");
            }

            Check("Chats", before.ChatCount, after.ChatCount);
            Check("Plugin data modules", before.PluginDataModuleCount, after.PluginDataModuleCount);
            Check("Expected replies", before.ExpectedReplyCount, after.ExpectedReplyCount);
            Check("Recent chat members", before.RecentChatMemberCount, after.RecentChatMemberCount);
            Check("Stat types", before.StatTypeCount, after.StatTypeCount);
            Check("Stat slices", before.StatSliceCount, after.StatSliceCount);
            Check("Xyzzy questions", before.XyzzyQuestionCount, after.XyzzyQuestionCount);
            Check("Xyzzy answers", before.XyzzyAnswerCount, after.XyzzyAnswerCount);
            Check("Xyzzy packs", before.XyzzyPackCount, after.XyzzyPackCount);
            Check("Xyzzy players", before.XyzzyTotalPlayersAcrossChats, after.XyzzyTotalPlayersAcrossChats);
            Check("Quotes", before.QuoteCount, after.QuoteCount);
            Check("Birthdays", before.BirthdayCount, after.BirthdayCount);

            var allModules = before.ChatsWithModuleData.Keys.Union(after.ChatsWithModuleData.Keys);
            foreach (var module in allModules.OrderBy(x => x))
            {
                Check($"Chats with {module}", before.ChatsWithModuleData.GetValueOrDefault(module), after.ChatsWithModuleData.GetValueOrDefault(module));
            }

            return diffs;
        }
    }
}
