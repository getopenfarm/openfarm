# Contract: `IPrinterConnectorFactory` and the Verify-Connection request shape

**Supersedes**: an earlier version of this document describing IP-range-based selection. The 2026-07-17
clarification follow-up replaced that with Domain-catalog-based (Brand/ModelCode) selection — see
research.md §§1-5 for the full rationale. This revision also documents one small HTTP-facing contract
change (`VerifyPrinterConnectionRequest` gains `Brand`/`ModelCode`), since Brand/Model selection is no
longer inferrable from the IP address alone.

## `OpenFarm.Application.Printers.PrinterConnectionDetails` (changed — two new fields)

```csharp
namespace OpenFarm.Application.Printers;

public sealed record PrinterConnectionDetails(string Brand, string ModelCode, string IpAddress, string SerialNumber, string AccessCode);
```

`Brand`/`ModelCode` are additive; every existing field is unchanged. Real connectors
(`BambuLanConnector`, `BambuCameraRelay`) never read `Brand`/`ModelCode` — only
`PrinterConnectorFactory`'s routing logic does.

## `OpenFarm.Application.Printers.IPrinterConnectorFactory` (interface itself — unchanged)

```csharp
namespace OpenFarm.Application.Printers;

public interface IPrinterConnectorFactory
{
    /// <summary>
    /// Resolves the <see cref="IPrinterConnector"/> that should serve <paramref name="details"/> —
    /// the real Bambu LAN connector for an ordinary printer, or the built-in simulator when
    /// <paramref name="details"/>'s Brand/ModelCode is the catalog's "System"/"Simulation" entry.
    /// Callers never need to know which one they got.
    /// </summary>
    IPrinterConnector CreateConnector(PrinterConnectionDetails details);

    /// <summary>
    /// Resolves the <see cref="IPrinterCameraRelay"/> matching <paramref name="details"/>, using the
    /// same selection rule as <see cref="CreateConnector"/>.
    /// </summary>
    IPrinterCameraRelay CreateCameraRelay(PrinterConnectionDetails details);
}
```

Method signatures are byte-for-byte unchanged from before the reversal — only the data flowing through
the already-existing `details` parameter grew two fields every call site already had in scope
(research.md §2).

**Consumers** (unchanged from before, since none of their call shapes change):
- `VerifyPrinterConnectionEndpoint` (Api) — calls `CreateConnector(details).VerifyAsync(details, ct)`;
  now builds `details` from `req.Brand`/`req.ModelCode` too (see below).
- `RegisterPrinterCommandHandler`, `EditPrinterConnectionCommandHandler` (Application) — same
  verify-before-persist call, now including `command.Brand`/`command.ModelCode` or the already-loaded
  `printer.Brand`/`printer.ModelCode` when constructing `details`.
- `PrinterConnectionBackgroundService.RunConnectionLoopAsync` / `RunCameraLoopAsync` (Infrastructure) —
  resolve once `details` is known (now including the already-loaded printer's Brand/ModelCode).

**Pre/post-conditions**: Pure function of `details` — no I/O, no async, never throws for a well-formed
`PrinterConnectionDetails`. Idempotent: calling it twice with the same `details` for the same printer
during its connection lifetime returns functionally equivalent connectors (the simulator's singleton
instance is shared; a fresh `BambuLanConnector` is constructed each time, unchanged from before).

## HTTP-facing change: `VerifyPrinterConnectionRequest` gains `Brand`/`ModelCode`

```csharp
public sealed class VerifyPrinterConnectionRequest
{
    public string Brand { get; set; } = string.Empty;      // NEW
    public string ModelCode { get; set; } = string.Empty;  // NEW
    public string IpAddress { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string AccessCode { get; set; } = string.Empty;
}
```

`VerifyPrinterConnectionRequestValidator` gains matching `NotEmpty()` rules for both fields, mirroring
`RegisterPrinterRequestValidator`'s existing `Brand`/`ModelCode` rules (which needs no change — it
already validates these fields). `RegisterPrinterRequest`/`RegisterPrinterCommand` need no shape change;
they already carry `Brand`/`ModelCode`.

**Consequence**: this is an OpenAPI-surface change (the one endpoint's request DTO gains two required
fields), so the Orval-generated frontend client (`web/src/lib/api/generated`) must be regenerated before
`AddPrinterDialog.tsx`'s updated verify-call payload compiles against the generated hook's type.

## New (Infrastructure-internal, not Application-owned): `SimulatorPrinterConnector`'s test-only trigger

Unchanged from before the reversal — this was never about *selection*, only about *driving* an
already-connected simulated printer's lifecycle in tests:

```csharp
namespace OpenFarm.Infrastructure.Printers.Simulator;

public sealed class SimulatorPrinterConnector : IPrinterConnector
{
    // IPrinterConnector members (VerifyAsync, ConnectAsync) — unchanged shape.

    /// <summary>
    /// Test-only entry point standing in for a future real job-dispatch command. Flips
    /// <paramref name="serialNumber"/>'s simulated state to "job running". No-op if the serial has
    /// no active connection.
    /// </summary>
    public void StartSimulatedJob(string serialNumber);
}
```
