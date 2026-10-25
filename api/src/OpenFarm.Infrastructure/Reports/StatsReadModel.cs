using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Reports;
using OpenFarm.Domain.Printers;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Reports;

/// <summary>
/// The stats SQL. Every figure is summed in Postgres, so only per-day and per-printer totals leave the database. Days are
/// local midnights in the range's zone; a 23 or 25 hour day stays one day.
/// ponytail: prints and activity are scanned by time without a dedicated index, which is milliseconds at a farm's few
/// thousand prints a year; index Prints.EndedAtUtc past ~100k prints, and add an hourly rollup only if a request passes ~300 ms.
/// </summary>
public sealed class StatsReadModel(OpenFarmDbContext dbContext) : IStatsReadModel
{
    public async Task<StatsData> ReadAsync(StatsRange range, CancellationToken cancellationToken)
    {
        // Npgsql writes timestamptz parameters only at offset 0.
        var from = range.From.ToUniversalTime();
        var to = range.To.ToUniversalTime();
        var now = range.NowUtc.ToUniversalTime();
        var zone = range.TimeZoneId;

        // Prints that started and ended in the range, by local end day and by printer: the same rows the materials report counts.
        var prints = await dbContext.Database.SqlQuery<PrintsRow>($"""
            WITH ended AS (
                SELECT x."PrinterId", x."Status", x."StartedAtUtc", x."EndedAtUtc", j."PlateQuantity",
                       (x."EndedAtUtc" AT TIME ZONE {zone})::date AS day,
                       (SELECT sum(f."GramsConsumed") FROM "PrintFilaments" f WHERE f."PrintId" = x."Id") AS grams
                FROM "Prints" x
                LEFT JOIN "PrintJobs" j ON j."Id" = x."Id"
                WHERE x."StartedAtUtc" IS NOT NULL AND x."Status" IN ('Completed', 'Failed', 'Cancelled')
                  AND x."EndedAtUtc" >= {from} AND x."EndedAtUtc" < {to}
            )
            SELECT grouping(e."PrinterId") = 0 AS "ByPrinter", e.day AS "Day", e."PrinterId" AS "PrinterId",
                   count(*) FILTER (WHERE e."Status" = 'Completed')::int AS "Completed",
                   count(*) FILTER (WHERE e."Status" = 'Failed')::int AS "Failed",
                   count(*) FILTER (WHERE e."Status" = 'Cancelled')::int AS "Cancelled",
                   coalesce(sum(e.grams) FILTER (WHERE e."Status" = 'Completed'), 0) AS "UsedGrams",
                   coalesce(sum(e.grams) FILTER (WHERE e."Status" <> 'Completed'), 0) AS "WasteGrams",
                   coalesce(sum(e."PlateQuantity") FILTER (WHERE e."Status" = 'Completed'), 0)::int AS "Units",
                   coalesce(sum(extract(epoch FROM e."EndedAtUtc" - e."StartedAtUtc")) FILTER (WHERE e."Status" <> 'Completed'), 0)::float8 / 3600
                       AS "WasteHours"
            FROM ended e
            GROUP BY GROUPING SETS ((e.day), (e."PrinterId"))
            """).ToListAsync(cancellationToken);

        // Each printer's stretches clipped to each local day; the open stretch on the printer row runs to now.
        var activity = await dbContext.Database.SqlQuery<ActivityRow>($"""
            WITH days AS (
                SELECT d::date AS day,
                       greatest(d AT TIME ZONE {zone}, {from}) AS lo,
                       least((d + interval '1 day') AT TIME ZONE {zone}, {to}) AS hi
                FROM generate_series(({from} AT TIME ZONE {zone})::date::timestamp,
                                     (({to} AT TIME ZONE {zone}) - interval '1 microsecond')::date::timestamp,
                                     interval '1 day') AS d
            ),
            stretches AS (
                SELECT s."PrinterId", s."Activity", s."StartedAtUtc" AS lo, s."EndedAtUtc" AS hi
                FROM "PrinterActivitySpans" s
                WHERE s."EndedAtUtc" > {from} AND s."StartedAtUtc" < {to}
                UNION ALL
                SELECT p."Id", p."Activity", p."ActivitySinceUtc", {now}
                FROM "Printers" p
                WHERE p."ActivitySinceUtc" < {now} AND p."ActivitySinceUtc" < {to} AND {now} > {from}
            )
            SELECT grouping(s."PrinterId") = 0 AS "ByPrinter", d.day AS "Day", s."PrinterId" AS "PrinterId", s."Activity" AS "Activity",
                   sum(extract(epoch FROM least(s.hi, d.hi) - greatest(s.lo, d.lo)))::float8 / 3600 AS "Hours"
            FROM days d
            JOIN stretches s ON s.lo < d.hi AND s.hi > d.lo
            GROUP BY GROUPING SETS ((d.day, s."Activity"), (s."PrinterId", s."Activity"))
            """).ToListAsync(cancellationToken);

        var medians = await dbContext.Database.SqlQuery<MediansRow>($"""
            SELECT
                (SELECT percentile_cont(0.5) WITHIN GROUP (ORDER BY extract(epoch FROM x."StartedAtUtc" - x."CreatedAtUtc")::float8 / 60)
                   FROM "Prints" x
                   WHERE x."Source" = 'Dispatched' AND x."StartedAtUtc" >= {from} AND x."StartedAtUtc" < {to}) AS "QueueWaitP50Minutes",
                (SELECT percentile_cont(0.95) WITHIN GROUP (ORDER BY extract(epoch FROM x."StartedAtUtc" - x."CreatedAtUtc")::float8 / 60)
                   FROM "Prints" x
                   WHERE x."Source" = 'Dispatched' AND x."StartedAtUtc" >= {from} AND x."StartedAtUtc" < {to}) AS "QueueWaitP95Minutes",
                (SELECT percentile_cont(0.5) WITHIN GROUP (
                            ORDER BY x."ActualDurationMinutes"::float8 / nullif((j."ProductionSnapshotJson" ->> 'EstimatedPrintTimeMinutes')::float8, 0))
                   FROM "Prints" x
                   JOIN "PrintJobs" j ON j."Id" = x."Id"
                   WHERE x."Status" = 'Completed' AND x."ActualDurationMinutes" > 0
                     AND x."EndedAtUtc" >= {from} AND x."EndedAtUtc" < {to}) AS "EstimateRatio",
                (SELECT percentile_cont(0.5) WITHIN GROUP (ORDER BY extract(epoch FROM s."EndedAtUtc" - s."StartedAtUtc")::float8 / 60)
                   FROM "PrinterActivitySpans" s
                   WHERE s."Activity" = 'AwaitingClear' AND s."EndedAtUtc" >= {from} AND s."EndedAtUtc" < {to}) AS "PlateClearMinutes"
            """).SingleAsync(cancellationToken);

        var failureCodes = await dbContext.Database.SqlQuery<FailureCodeRow>($"""
            SELECT a ->> 'Code' AS "Code", min(a ->> 'Message') AS "Message", count(DISTINCT x."Id")::int AS "Prints"
            FROM "Prints" x
            CROSS JOIN LATERAL jsonb_array_elements(x."StopAlerts") AS a
            WHERE x."Status" = 'Failed' AND x."EndedAtUtc" >= {from} AND x."EndedAtUtc" < {to} AND a ->> 'Code' IS NOT NULL
            GROUP BY 1
            ORDER BY 3 DESC, 1
            LIMIT 5
            """).ToListAsync(cancellationToken);

        var printers = await dbContext.Printers.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new StatsPrinter(p.Id, p.Name, p.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var stretches = activity
            .Select(a => (Row: a, Known: Enum.TryParse<PrinterActivity>(a.Activity, out var parsed), Activity: parsed))
            .Where(a => a.Known)
            .ToList();
        return new StatsData(
            prints.Where(p => !p.ByPrinter && p.Day is not null).Select(p => (p.Day!.Value, p.ToPrints())).ToList(),
            prints.Where(p => p.ByPrinter).Select(p => (p.PrinterId, p.ToPrints())).ToList(),
            stretches.Where(a => !a.Row.ByPrinter && a.Row.Day is not null).Select(a => (a.Row.Day!.Value, a.Activity, a.Row.Hours)).ToList(),
            stretches.Where(a => a.Row.ByPrinter && a.Row.PrinterId is not null).Select(a => (a.Row.PrinterId!.Value, a.Activity, a.Row.Hours)).ToList(),
            printers,
            new StatsMedians(medians.QueueWaitP50Minutes, medians.QueueWaitP95Minutes, medians.EstimateRatio, medians.PlateClearMinutes),
            failureCodes.Select(c => new FailureCode(c.Code, c.Message, c.Prints)).ToList());
    }

    internal sealed class PrintsRow
    {
        public bool ByPrinter { get; set; }

        public DateOnly? Day { get; set; }

        public Guid? PrinterId { get; set; }

        public int Completed { get; set; }

        public int Failed { get; set; }

        public int Cancelled { get; set; }

        public decimal UsedGrams { get; set; }

        public decimal WasteGrams { get; set; }

        public int Units { get; set; }

        public double WasteHours { get; set; }

        public StatsPrints ToPrints() => new(Completed, Failed, Cancelled, UsedGrams, WasteGrams, Units, WasteHours);
    }

    internal sealed class ActivityRow
    {
        public bool ByPrinter { get; set; }

        public DateOnly? Day { get; set; }

        public Guid? PrinterId { get; set; }

        public string Activity { get; set; } = string.Empty;

        public double Hours { get; set; }
    }

    internal sealed class MediansRow
    {
        public double? QueueWaitP50Minutes { get; set; }

        public double? QueueWaitP95Minutes { get; set; }

        public double? EstimateRatio { get; set; }

        public double? PlateClearMinutes { get; set; }
    }

    internal sealed class FailureCodeRow
    {
        public string Code { get; set; } = string.Empty;

        public string? Message { get; set; }

        public int Prints { get; set; }
    }
}
