using MessagePack;
using Moonlight.API.Data;
using Moonlight.API.Data.Enum;

namespace Moonlight.API.Dto.Group;

[MessagePackObject(keyAsPropertyName: true)]
public record GroupPairUserPermissionDto(GroupData Group, UserData User, GroupUserPreferredPermissions GroupPairPermissions) : GroupPairDto(Group, User);