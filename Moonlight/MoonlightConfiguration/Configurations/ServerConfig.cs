using Moonlight.MoonlightConfiguration.Models;
using Moonlight.WebAPI;

namespace Moonlight.MoonlightConfiguration.Configurations;

[Serializable]
public class ServerConfig : IMoonlightConfiguration
{
    public int CurrentServer { get; set; } = 0;

    public List<ServerStorage> ServerStorage { get; set; } = new()
    {
        { new ServerStorage() { ServerName = ApiController.MainServer, ServerUri = ApiController.MainServiceUri } },
    };

    public bool SendCensusData { get; set; } = false;
    public bool ShownCensusPopup { get; set; } = false;

    public int Version { get; set; } = 2;
}