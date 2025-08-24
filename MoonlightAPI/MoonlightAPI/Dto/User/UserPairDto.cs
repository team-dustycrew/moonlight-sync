using MessagePack;
using Moonlight.API.Data;
using Moonlight.API.Data.Enum;

namespace Moonlight.API.Dto.User;

[MessagePackObject(keyAsPropertyName: true)]
public record UserFullPairDto(UserData User, IndividualPairStatus IndividualPairStatus, List<string> Groups, UserPermissions OwnPermissions, UserPermissions OtherPermissions) : UserDto(User)
{
    public UserPermissions OwnPermissions { get; set; } = OwnPermissions;
    public UserPermissions OtherPermissions { get; set; } = OtherPermissions;
    public IndividualPairStatus IndividualPairStatus { get; set; } = IndividualPairStatus;
}

[MessagePackObject(keyAsPropertyName: true)]
public record UserPairDto(UserData User, IndividualPairStatus IndividualPairStatus, UserPermissions OwnPermissions, UserPermissions OtherPermissions) : UserDto(User)
{
    public UserPermissions OwnPermissions { get; set; } = OwnPermissions;
    public UserPermissions OtherPermissions { get; set; } = OtherPermissions;
    public IndividualPairStatus IndividualPairStatus { get; set; } = IndividualPairStatus;
}