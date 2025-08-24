using MessagePack;
using Moonlight.API.Data;

namespace Moonlight.API.Dto.User;

[MessagePackObject(keyAsPropertyName: true)]
public record OnlineUserIdentDto(UserData User, string Ident) : UserDto(User);