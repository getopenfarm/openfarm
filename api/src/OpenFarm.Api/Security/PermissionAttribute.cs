namespace OpenFarm.Api.Security;

[AttributeUsage(AttributeTargets.Class)]
public sealed class PermissionAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}
