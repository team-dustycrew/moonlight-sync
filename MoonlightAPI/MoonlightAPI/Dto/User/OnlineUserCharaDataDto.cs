using MessagePack;
using Moonlight.API.Data;

namespace Moonlight.API.Dto.User;

[MessagePackObject(keyAsPropertyName: true)]
public record OnlineUserCharaDataDto(UserData User, CharacterData CharaData) : UserDto(User);