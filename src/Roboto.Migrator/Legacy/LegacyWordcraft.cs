using System.Xml.Serialization;

namespace Roboto.Migrator.Legacy;

/// <summary>Global, not per-chat - matches WordcraftStore's own single shared word list.</summary>
[XmlType("mod_wordcraft_data")]
public sealed class LegacyWordcraftData : LegacyModuleData
{
    public List<string> words = [];
}
