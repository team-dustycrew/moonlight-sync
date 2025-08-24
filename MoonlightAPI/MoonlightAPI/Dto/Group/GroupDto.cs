using MessagePack;
using Moonlight.API.Data;

namespace Moonlight.API.Dto.Group;

[MessagePackObject(keyAsPropertyName: true)]
public record GroupDto(GroupData Group)
{
    public GroupData Group { get; set; } = Group;
    public string GID => Group.GID;
    public string? GroupAlias => Group.Alias;
    public string GroupAliasOrGID => Group.AliasOrGID;
}