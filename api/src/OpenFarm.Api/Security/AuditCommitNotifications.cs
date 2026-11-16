using System.Data.Common;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OpenFarm.Api.Hubs;
using OpenFarm.Application.AuditLog;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.Security;

public sealed partial class AuditChangeNotifier(IFarmEventClients clients, ILogger<AuditChangeNotifier> logger) : IAuditChangeNotifier
{
    public async Task NotifyAsync()
    {
        try { await clients.SendAsync("AuditChanged", new { }); }
        catch (Exception error) { LogPublishFailed(logger, error); }
    }
    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not publish committed audit refresh")]
    private static partial void LogPublishFailed(ILogger logger, Exception error);
}

/// <summary>Outer transactions publish only when committed, never on an intermediate SaveChanges.</summary>
public sealed class AuditCommitNotifications(IAuditChangeNotifier notifier) : DbTransactionInterceptor
{
    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (eventData.Context is OpenFarmDbContext db && db.TakeAuditNotification()) notifier.NotifyAsync().GetAwaiter().GetResult();
    }

    public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is OpenFarmDbContext db && db.TakeAuditNotification()) await notifier.NotifyAsync();
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (eventData.Context is OpenFarmDbContext db) db.TakeAuditNotification();
    }

    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is OpenFarmDbContext db) db.TakeAuditNotification();
        return Task.CompletedTask;
    }
}
