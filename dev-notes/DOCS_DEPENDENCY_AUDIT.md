# Documentation dependency audit

The Docusaurus site is a static build. Its dependency graph is separate from
`web/` and is not copied into the production application image. Review these
exceptions when updating Docusaurus and remove them when its consumers can
upgrade compatibly.

`docs/package.json` overrides `tinypool`, `serialize-javascript`, and `uuid` to
patched versions. The docs audit excludes two advisories on tooling dependencies:

- `GHSA-gcqx-w48m-w2h4`: deepmerge 4.3.1, the same unpatched CLI dependency
  exception already recorded for the web toolchain in `DEPENDENCY_AUDIT.md`.
  Here it is reached through the preset's SVGR/SVGO build tooling. Guides do not
  accept untrusted SVG imports, and the static site has no server-side merge API.
- `GHSA-g3hv-75wg-m249`: minimatch 3.1.5 through serve-handler, used by
  Docusaurus's local `serve` preview. Patched minimatch 11 changes its CommonJS
  export from a callable function to a named export, so overriding it would break
  serve-handler's `minimatch(path, pattern)` call. No patched compatible branch
  is currently available. This site has no custom serve-handler glob rules and
  production hosting serves only the generated files, without serve-handler.

Run `bun run audit` in `docs/`. The weekly dependency audit workflow checks this
graph separately from the application. Do not use a blanket major-version
override for minimatch without updating and verifying its consumer.
