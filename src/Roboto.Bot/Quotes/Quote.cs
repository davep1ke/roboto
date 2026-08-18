namespace Roboto.Bot.Quotes;

/// <summary>One or more lines exchanged in a conversation, quoted verbatim - legacy's obsolete
/// single-line-only shape (mod_quote_quote) is dropped, not ported; every quote is just a
/// one-or-more-line list now (a single-line quote is simply a list with one entry).</summary>
public sealed class Quote
{
    public List<QuoteLine> Lines { get; set; } = [];
    public DateTime On { get; set; } = DateTime.UtcNow;

    public string GetText()
    {
        var text = $"On {On:g}\n";
        foreach (var line in Lines)
        {
            text += $"*{line.By}* : {line.Text}\n";
        }

        return text;
    }
}
