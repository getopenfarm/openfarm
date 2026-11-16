namespace OpenFarm.Application.AuditLog;

/// <summary>Explicit execution attribution for background work and nested operations.</summary>
public sealed class AuditContextScope : IDisposable
{
    private static readonly AsyncLocal<AuditContext?> Value = new();
    private readonly AuditContext? _previous;
    public static AuditContext? Current => Value.Value;
    public AuditContextScope(AuditContext context) { _previous = Value.Value; Value.Value = context; }
    public void Dispose() => Value.Value = _previous;
}
