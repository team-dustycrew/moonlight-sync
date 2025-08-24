using Moonlight.MoonlightConfiguration.Models;

namespace Moonlight.MoonlightConfiguration.Configurations;

public class CharaDataConfig : IMoonlightConfiguration
{
    public bool OpenMoonlightHubOnGposeStart { get; set; } = false;
    public string LastSavedCharaDataLocation { get; set; } = string.Empty;
    public Dictionary<string, CharaDataFavorite> FavoriteCodes { get; set; } = [];
    public bool DownloadMcdDataOnConnection { get; set; } = true;
    public int Version { get; set; } = 0;
    public bool NearbyOwnServerOnly { get; set; } = false;
    public bool NearbyIgnoreHousingLimitations { get; set; } = false;
    public bool NearbyDrawWisps { get; set; } = true;
    public int NearbyDistanceFilter { get; set; } = 100;
    public bool NearbyShowOwnData { get; set; } = false;
    public bool ShowHelpTexts { get; set; } = true;
    public bool NearbyShowAlways { get; set; } = false;
}