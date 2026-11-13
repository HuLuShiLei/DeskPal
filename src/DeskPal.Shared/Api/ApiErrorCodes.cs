namespace DeskPal.Shared.Api;

/// <summary>
/// Error codes in <see cref="ApiError"/> and in the server's HubException messages. The client has a message for each
/// code in every language it supports; codes must not be renamed once released.
/// </summary>
public static class ApiErrorCodes
{
    public const string UserNameInvalid = "auth.userNameInvalid";
    public const string PasswordTooShort = "auth.passwordTooShort";
    public const string UserNameTaken = "auth.userNameTaken";
    public const string WrongPassword = "auth.wrongPassword";
    public const string AccountMissing = "auth.accountMissing";
    public const string DisplayNameInvalid = "auth.displayNameInvalid";

    public const string RoomNotMember = "room.notMember";
    public const string RoomNameInvalid = "room.nameInvalid";

    /// <summary>Args: limit.</summary>
    public const string RoomOwnLimit = "room.ownLimit";
    public const string RoomInviteInvalid = "room.inviteInvalid";
    public const string RoomAlreadyMember = "room.alreadyMember";
    public const string RoomBanned = "room.banned";

    /// <summary>Args: limit.</summary>
    public const string RoomFull = "room.full";
    public const string RoomOwnerCannotLeave = "room.ownerCannotLeave";
    public const string RoomOwnerOnlyKick = "room.ownerOnlyKick";
    public const string RoomCannotKickOwner = "room.cannotKickOwner";
    public const string RoomMemberMissing = "room.memberMissing";
    public const string RoomOwnerOnlyTransfer = "room.ownerOnlyTransfer";
    public const string RoomTransferTarget = "room.transferTarget";
    public const string RoomOwnerOnlyRename = "room.ownerOnlyRename";
    public const string RoomOwnerOnlyDissolve = "room.ownerOnlyDissolve";
    public const string RoomStatsDaysInvalid = "room.statsDaysInvalid";
    public const string RoomCannotBlockSelf = "room.cannotBlockSelf";

    public const string RulePatternInvalid = "rule.patternInvalid";
    public const string RuleCategoryRequired = "rule.categoryRequired";

    /// <summary>Args: limit.</summary>
    public const string RuleLimit = "rule.limit";
    public const string SharingOff = "sharing.off";

    public const string HubUnsupportedType = "hub.unsupportedType";
    public const string HubNotMember = "hub.notMember";

    public const string LimitArg = "limit";
}