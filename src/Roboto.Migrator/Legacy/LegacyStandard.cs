using System.Xml.Serialization;

namespace Roboto.Migrator.Legacy;

/// <summary>Quiet hours, stored as tick counts (long), not TimeSpan directly - matches
/// mod_standard.cs's mod_standard_chatdata exactly (x_quietHoursStartTime/EndTime backing fields
/// for its TimeSpan-typed properties).</summary>
[XmlType("mod_standard_chatdata")]
public sealed class LegacyStandardChatData : LegacyModuleChatData
{
    public long x_quietHoursStartTime = TimeSpan.MinValue.Ticks;
    public long x_quietHoursEndTime = TimeSpan.MinValue.Ticks;
}

/// <summary>Global core data (last-save/background bookkeeping only) - deserialized so the
/// xsi:type resolves, not because this importer needs anything from it.</summary>
[XmlType("mod_standard_data")]
public sealed class LegacyStandardData : LegacyModuleData;
