namespace Moonlight.MoonlightConfiguration.Models;

public class ServerNotesStorage
{
    public Dictionary<Guid, string> GidServerComments { get; set; } = new();
    public Dictionary<Guid, string> UidServerComments { get; set; } = new();
}