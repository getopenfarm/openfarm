namespace OpenFarm.Api.Security;

[AttributeUsage(AttributeTargets.Class)]
public sealed class PermissionAttribute(string permission, params string[] additionalPermissions) : Attribute
{
    public string Permission { get; } = permission;
    public IReadOnlyList<string> AdditionalPermissions { get; } = additionalPermissions;
}
