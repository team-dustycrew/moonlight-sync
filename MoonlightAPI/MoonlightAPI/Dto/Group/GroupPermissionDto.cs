using MessagePack;
using Moonlight.API.Data;
using Moonlight.API.Data.Enum;

namespace Moonlight.API.Dto.Group;

[MessagePackObject(keyAsPropertyName: true)]
public record GroupPermissionDto(GroupData Group, GroupPermissions Permissions) : GroupDto(Group);