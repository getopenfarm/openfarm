# Dependency audit

Run `bun install --frozen-lockfile && bun run audit` from `web/` to reproduce
the frontend check in `.github/workflows/audit.yml`. The audit checks all
dependencies and severity levels, with the single exception below.

The .NET step audits direct and transitive NuGet dependencies during a fresh
restore using `NuGetAudit=true` and `NuGetAuditMode=all`. Warnings NU1901 through
NU1904 are errors, so every known vulnerability severity fails the job; the
subsequent package listing provides a readable report. A successful audit means
the configured feeds report no known vulnerabilities, rather than guaranteeing
that dependencies contain no undiscovered issues.

## Temporary exception: deepmerge

- Advisory: [GHSA-gcqx-w48m-w2h4](https://github.com/advisories/GHSA-gcqx-w48m-w2h4)
  (CVE-2026-93753), low severity prototype poisoning in `deepmerge`.
- Reviewed: 2026-11-03. No patched release is available; upstream's latest
  release is 4.3.1.
- Dependency path: `shadcn` and `shadcn > @shadcn/registry` depend on `deepmerge`.
  OpenFarm imports only `shadcn/tailwind.css`; the CLI's JavaScript and
  `deepmerge` are not imported by the application. The CLI is developer tooling
  and should only be used with trusted registry/configuration inputs.
- The `audit` script excludes only this advisory ID. All other advisories,
  including low-severity findings, still fail the check. Run plain `bun audit`
  to include the accepted finding.
- Review this exception during dependency updates and remove the ignore when
  upstream publishes a fix or removes the dependency.

## Transitive security overrides

`web/package.json` uses overrides where upstream dependency ranges or previously
locked versions retain vulnerable packages. `minimatch` is overridden to v11
because GHSA-g3hv-75wg-m249 affects versions below 11.0.1, including v10 used
by shadcn's `ts-morph` tooling. Verify the shadcn CLI and API generator as well
as frontend tests, lint, and build when changing these overrides.
