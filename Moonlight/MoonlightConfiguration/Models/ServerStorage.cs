using Microsoft.AspNetCore.Http.Connections;

namespace Moonlight.MoonlightConfiguration.Models;

[Serializable]
public class ServerStorage
{
    public List<Authentication> Authentications { get; set; } = [];
    public bool FullPause { get; set; } = false;
    public Dictionary<int, SecretKey> SecretKeys { get; set; } = [];
    public string ServerName { get; set; } = string.Empty;
    public string ServerUri { get; set; } = string.Empty;
    public HttpTransportType HttpTransportType { get; set; } = HttpTransportType.WebSockets;
    public bool ForceWebSockets { get; set; } = false;
}