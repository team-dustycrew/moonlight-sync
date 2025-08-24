using MessagePack;
using Moonlight.API.Data;
using Moonlight.API.Data.Enum;

namespace Moonlight.API.Dto.Group;

[MessagePackObject(keyAsPropertyName: true)]
public record GroupPairUserInfoDto(GroupData Group, UserData User, GroupPairUserInfo GroupUserInfo) : GroupPairDto(Group, User);