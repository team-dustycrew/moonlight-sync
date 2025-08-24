using MessagePack;
using Moonlight.API.Data;
using Moonlight.API.Data.Enum;

namespace Moonlight.API.Dto.Group;

[MessagePackObject(keyAsPropertyName: true)]
public record GroupPasswordDto(GroupData Group, string Password) : GroupDto(Group);

[MessagePackObject(keyAsPropertyName: true)]
public record GroupJoinDto(GroupData Group, string Password, GroupUserPreferredPermissions GroupUserPreferredPermissions) : GroupPasswordDto(Group, Password);