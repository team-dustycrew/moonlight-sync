using Moonlight.MoonlightConfiguration.Models;

namespace Moonlight.MoonlightConfiguration.Configurations;

public class UidNotesConfig : IMoonlightConfiguration
{
    public Dictionary<string, ServerNotesStorage> ServerNotes { get; set; } = new(StringComparer.Ordinal);
    public int Version { get; set; } = 0;
}
