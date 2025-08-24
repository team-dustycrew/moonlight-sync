using MessagePack;
using Moonlight.API.Data;

namespace Moonlight.API.Dto.User;

[MessagePackObject(keyAsPropertyName: true)]
public record UserDto(UserData User);