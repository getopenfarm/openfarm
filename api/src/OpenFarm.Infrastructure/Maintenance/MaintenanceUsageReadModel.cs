using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Maintenance;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Maintenance;

/// <summary>
/// Usage per enrollment window in one query. Hours: Printing stretches clipped to the window (closed spans plus the
/// printer's open stretch), or the summed durations of ended prints when that is more, since prints whose end was inferred
/// write no span. Prints: Completed, Failed and Cancelled prints started in the window, external ones included.
/// </summary>
public sealed class MaintenanceUsageReadModel(OpenFarmDbContext dbContext) : IMaintenanceUsageReadModel
{
    public async Task<IReadOnlyDictionary<Guid, MaintenanceMeasuredUsage>> ReadAsync(
        IReadOnlyList<MaintenanceUsageWindow> windows, CancellationToken cancellationToken)
    {
        if (windows.Count == 0) return new Dictionary<Guid, MaintenanceMeasuredUsage>();

        // Npgsql writes timestamptz parameters only at offset 0. Each array is passed once, through the CTE.
        var ids = windows.Select(w => w.EnrollmentId).ToArray();
        var printerIds = windows.Select(w => w.PrinterId).ToArray();
        var since = windows.Select(w => w.Since.ToUniversalTime()).ToArray();
        var until = windows.Select(w => w.Until.ToUniversalTime()).ToArray();

        var rows = await dbContext.Database.SqlQuery<UsageRow>($"""
            WITH w AS (
                SELECT * FROM unnest({ids}, {printerIds}, {since}, {until}) AS w(id, printer_id, lo, hi)
            )
            SELECT w.id AS "EnrollmentId",
                   coalesce(a.printing, 0)::float8 / 3600 AS "SpanHours",
                   coalesce(a.unknown, 0)::float8 / 3600 AS "UntrackedHours",
                   coalesce(x.seconds, 0)::float8 / 3600 AS "PrintHours",
                   coalesce(x.started, 0)::int AS "Prints"
            FROM w
            CROSS JOIN LATERAL (
                SELECT sum(extract(epoch FROM least(s.hi, w.hi) - greatest(s.lo, w.lo))) FILTER (WHERE s.activity = 'Printing') AS printing,
                       sum(extract(epoch FROM least(s.hi, w.hi) - greatest(s.lo, w.lo))) FILTER (WHERE s.activity = 'Unknown') AS unknown
                FROM (
                    SELECT sp."Activity" AS activity, sp."StartedAtUtc" AS lo, sp."EndedAtUtc" AS hi
                    FROM "PrinterActivitySpans" sp
                    WHERE sp."PrinterId" = w.printer_id AND sp."EndedAtUtc" > w.lo AND sp."StartedAtUtc" < w.hi
                    UNION ALL
                    SELECT p."Activity", p."ActivitySinceUtc", w.hi
                    FROM "Printers" p
                    WHERE p."Id" = w.printer_id AND p."ActivitySinceUtc" < w.hi
                ) s
            ) a
            CROSS JOIN LATERAL (
                SELECT sum(extract(epoch FROM least(p."EndedAtUtc", w.hi) - greatest(p."StartedAtUtc", w.lo)))
                           FILTER (WHERE p."EndedAtUtc" > w.lo) AS seconds,
                       count(*) FILTER (WHERE p."StartedAtUtc" >= w.lo) AS started
                FROM "Prints" p
                WHERE p."PrinterId" = w.printer_id AND p."Status" IN ('Completed', 'Failed', 'Cancelled')
                  AND p."StartedAtUtc" < w.hi AND p."EndedAtUtc" IS NOT NULL
            ) x
            """).ToListAsync(cancellationToken);

        return rows.ToDictionary(
            r => r.EnrollmentId,
            r => new MaintenanceMeasuredUsage(
                Math.Round((decimal)Math.Max(r.SpanHours, r.PrintHours), 4), r.Prints, Math.Round((decimal)r.UntrackedHours, 4)));
    }

    internal sealed class UsageRow
    {
        public Guid EnrollmentId { get; set; }

        public double SpanHours { get; set; }

        public double UntrackedHours { get; set; }

        public double PrintHours { get; set; }

        public int Prints { get; set; }
    }
}
