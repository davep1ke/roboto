using System.Xml.Serialization;

namespace Roboto.Migrator.Legacy;

[XmlType("mod_quote_data")]
public sealed class LegacyQuoteChatData : LegacyModuleChatData
{
    public List<LegacyMultiQuote> multiquotes = [];
    public bool autoQuoteEnabled = true;
    public int autoQuoteHours = 24;
    public DateTime nextAutoQuoteAfter = DateTime.MinValue;
}

/// <summary>Global core data (last-background-update bookkeeping only) - deserialized so the
/// xsi:type resolves, not because this importer needs anything from it.</summary>
[XmlType("mod_quote_core_data")]
public sealed class LegacyQuoteCoreData : LegacyModuleData;

[XmlType("mod_quote_multiquote")]
public sealed class LegacyMultiQuote
{
    public List<LegacyQuoteLine> lines = [];
    public DateTime on = DateTime.UtcNow;
}

[XmlType("mod_quote_quote_line")]
public sealed class LegacyQuoteLine
{
    public string by = "";
    public string text = "";
}
