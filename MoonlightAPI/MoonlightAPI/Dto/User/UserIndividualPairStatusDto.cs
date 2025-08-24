using MessagePack;
using Moonlight.API.Data;
using Moonlight.API.Data.Enum;

namespace Moonlight.API.Dto.User;

[MessagePackObject(keyAsPropertyName: true)]
public record UserIndividualPairStatusDto(UserData User, IndividualPairStatus IndividualPairStatus) : UserDto(User);