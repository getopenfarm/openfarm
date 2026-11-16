namespace OpenFarm.Application.AuditLog;

public interface IAuditChangeNotifier
{
    Task NotifyAsync();
}
