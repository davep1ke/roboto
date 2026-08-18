namespace Roboto.Migrator;

/// <summary>Counts only - matches CLAUDE.md's explicit "validate with counts/checksums... rather
/// than eyeballing it" requirement. Produced identically for a dry run and a real write (both go
/// through the exact same import code, just against a throwaway vs. the real target store), so a
/// dry-run report is a trustworthy preview of what a real run will do, not a separate, potentially
/// drifting code path.</summary>
public sealed class ImportReport
{
    public string? BotUserName { get; set; }
    public int ChatsImported { get; set; }
    public int QuotesImported { get; set; }
    public int BirthdaysImported { get; set; }
    public int WordcraftWordsImported { get; set; }
    public int SteamPlayersImported { get; set; }
    public int SteamGamesImported { get; set; }
    public bool SteamApiKeyFound { get; set; }
    public bool SteamApiKeyCarried { get; set; }
    public int QuietHoursChatsImported { get; set; }

    public int QuestionCardsImported { get; set; }
    public int AnswerCardsImported { get; set; }
    public int MultiAnswerCardsImported { get; set; }
    public int UnmappableCardReferencesDropped { get; set; }

    public int XyzzyGamesImported { get; set; }
    public Dictionary<string, int> XyzzyGamesByStatus { get; set; } = [];

    public int PendingRepliesResumed { get; set; }
    public Dictionary<string, int> PendingRepliesDroppedByReason { get; set; } = [];

    public override string ToString()
    {
        var lines = new List<string>
        {
            $"Bot: {BotUserName}",
            $"Chats imported: {ChatsImported}",
            $"Quotes: {QuotesImported}, Birthdays: {BirthdaysImported}, Wordcraft words: {WordcraftWordsImported}",
            $"Quiet-hours chats: {QuietHoursChatsImported}",
            $"Steam: {SteamPlayersImported} tracked players, {SteamGamesImported} cached games, API key found={SteamApiKeyFound} carried={SteamApiKeyCarried}",
            $"Xyzzy catalog: {QuestionCardsImported} questions ({MultiAnswerCardsImported} multi-answer), {AnswerCardsImported} answers, {UnmappableCardReferencesDropped} unmappable card references dropped",
            $"Xyzzy games: {XyzzyGamesImported} ({string.Join(", ", XyzzyGamesByStatus.Select(kv => $"{kv.Key}={kv.Value}"))})",
            $"Pending replies resumed: {PendingRepliesResumed}",
        };

        if (PendingRepliesDroppedByReason.Count > 0)
        {
            lines.Add("Pending replies dropped:");
            lines.AddRange(PendingRepliesDroppedByReason.OrderByDescending(kv => kv.Value).Select(kv => $"  {kv.Key}: {kv.Value}"));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
