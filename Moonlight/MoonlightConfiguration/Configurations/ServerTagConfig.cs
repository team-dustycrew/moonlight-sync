using Moonlight.MoonlightConfiguration.Models;

namespace Moonlight.MoonlightConfiguration.Configurations;

public class ServerTagConfig : IMoonlightConfiguration
{
    public Dictionary<string, ServerTagStorage> ServerTagStorage { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int Version { get; set; } = 0;
}