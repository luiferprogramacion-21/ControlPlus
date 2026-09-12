using Microsoft.AspNetCore.Authorization;

namespace ControlPlus.Api.Authorization;

public sealed class PermissionAuthorizeAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "Permission:";

    public PermissionAuthorizeAttribute(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        Policy = $"{PolicyPrefix}{permission}";
    }
}
