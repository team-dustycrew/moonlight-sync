using MessagePack;

namespace Moonlight.API.Dto;

[MessagePackObject(keyAsPropertyName: true)]
public record SystemInfoDto
{
    public int OnlineUsers { get; set; }
}