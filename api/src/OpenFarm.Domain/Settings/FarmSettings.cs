using OpenFarm.Domain.Parts;

namespace OpenFarm.Domain.Settings;

/// <summary>The farm-wide settings singleton — exactly one row, created at first boot.</summary>
public sealed class FarmSettings
{
    /// <summary>Fixed key of the single settings row.</summary>
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000000001");

    private FarmSettings(DateTimeOffset createdAtUtc)
    {
        Id = SingletonId;
        DefaultSlicerEngine = SlicerEngine.OrcaSlicer;
        DefaultSlicerVersion = null;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    /// <summary>
    /// The engine half of the optional default <see cref="SlicerRef"/> pair (FR-007, research.md
    /// §5/§10). Meaningful only when <see cref="DefaultSlicerVersion"/> is also set — a
    /// <c>null</c> version means no explicit default has ever been set, and the effective default
    /// is computed at read time by <c>ResolveSlicerSelection</c> instead.
    /// </summary>
    public SlicerEngine DefaultSlicerEngine { get; private set; }

    /// <summary>The version half of the optional default pair; <c>null</c> ⇒ no explicit default set.</summary>
    public string? DefaultSlicerVersion { get; private set; }

    public string CurrencyCode { get; private set; } = "USD";

    public void SetCurrency(string code)
    {
        if (!SupportedCurrencies.Contains(code)) throw new ArgumentException("Unsupported currency.", nameof(code));
        CurrencyCode = code;
    }

    public static readonly string[] SupportedCurrencies = ["USD", "MYR", "EUR", "GBP", "SGD", "AUD", "CAD", "NZD", "JPY", "CNY", "HKD", "TWD", "KRW", "INR", "IDR", "THB", "PHP", "VND", "CHF", "SEK", "NOK", "DKK", "PLN", "CZK", "HUF", "TRY", "AED", "SAR", "ZAR", "BRL", "MXN"];

    public decimal? ElectricityPricePerKwh { get; private set; }

    public void SetElectricityPrice(decimal? pricePerKwh)
    {
        if (pricePerKwh < 0) throw new ArgumentOutOfRangeException(nameof(pricePerKwh));
        ElectricityPricePerKwh = pricePerKwh;
    }

    /// <summary>Percent added to filament, depreciation and electricity estimates for failed prints.</summary>
    public decimal FailureAllowancePercent { get; private set; }

    public void SetFailureAllowance(decimal percent)
    {
        if (percent is < 0 or >= 100) throw new ArgumentOutOfRangeException(nameof(percent));
        FailureAllowancePercent = percent;
    }

    public DateTimeOffset CreatedAtUtc { get; }

    public static FarmSettings Create() => new(DateTimeOffset.UtcNow);

    /// <summary>
    /// Sets the explicit farm default — the caller (<c>UpdateSlicingDefaultsCommandHandler</c>) MUST
    /// have already validated <paramref name="slicer"/> against the bundle via
    /// <c>ResolveSlicerSelection.EnsureAvailable</c> (FR-007); this method itself performs no
    /// availability check.
    /// </summary>
    public void SetDefaultSlicer(SlicerRef slicer)
    {
        ArgumentNullException.ThrowIfNull(slicer);
        DefaultSlicerEngine = slicer.Engine;
        DefaultSlicerVersion = slicer.Version;
    }

    /// <summary>The explicit default as a <see cref="SlicerRef"/>, or <c>null</c> when none has ever been set.</summary>
    public SlicerRef? DefaultSlicer => DefaultSlicerVersion is { } version ? new SlicerRef(DefaultSlicerEngine, version) : null;
}
