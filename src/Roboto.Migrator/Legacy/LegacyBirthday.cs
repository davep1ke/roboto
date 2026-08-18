using System.Xml.Serialization;

namespace Roboto.Migrator.Legacy;

[XmlType("mod_birthday_data")]
public sealed class LegacyBirthdayChatData : LegacyModuleChatData
{
    public List<LegacyBirthday> birthdays = [];
}

/// <summary>Global core data (last-processed-day bookkeeping only) - deserialized so the xsi:type
/// resolves, not because this importer needs anything from it.</summary>
[XmlType("mod_birthday_coredata")]
public sealed class LegacyBirthdayCoreData : LegacyModuleData;

public sealed class LegacyBirthday
{
    public string name = "";
    public DateTime birthday;
}
