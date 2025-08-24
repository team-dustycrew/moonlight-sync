using Moonlight.MoonlightConfiguration.Configurations;

namespace Moonlight.MNet;

[Serializable]
public class MNetConfig : IMoonlightConfiguration
{
    public string ApiKey { get; set; } = string.Empty;
    public string LastResolvedIdentity { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.MinValue;
    public string BaseUrl { get; set; } = "https://www.mnet.live";
    public int Version { get; set; } = 1;
}


