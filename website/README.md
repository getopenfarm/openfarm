# OpenFarm website and Pages deployment

The Astro site lives here; Docusaurus stays in `../docs`. Application code,
website content, and user guides all remain in `getopenfarm/openfarm`.

## Develop

Use Node.js 24 and Bun 1.3.14. From the repository root:

```bash
./dev.sh website     # http://localhost:4321
./dev.sh demo        # the embedded live demo, http://localhost:5174/app-preview/
./dev.sh docs        # http://localhost:3000/docs/ (a separate terminal)
```

The development servers are independent. In development the homepage embeds the
demo from `:5174`; links to `/docs/` work in the combined production preview below.

Edit `src/pages/index.astro` for homepage copy and `src/styles/global.css` for
styles. Keep claims aligned with `docs/content/` and the application. The page is
dark-first: its surfaces, blue and cyan are the app's dark tokens in
`../web/src/index.css`, so the page and the embedded app read as one material.
`public/openfarm.svg` is copied from the application mark in
`../web/public/favicon.svg`; update both when branding changes.

The homepage has a live demo, the run-to-printer flow, a feature index whose
"Show in demo" buttons drive the demo, the price statement, installation steps,
FAQ, and a final call to action. Keep model lists, deployment requirements, and
FAQ answers aligned with the guides. See the [website review](../dev-notes/WEBSITE_REVIEW.md)
for reference-site research and design decisions.

## Live demo

The demo is the real application, not a copy. `bun run build:demo` in `../web`
builds the app in Vite's `demo` mode; `tools/build-site.sh` publishes it at
`/app-preview/`, and `src/components/LiveDemo.astro` embeds it in an iframe.
Everything runs in the visitor's browser: `../web/src/demo/install.ts` answers
`/api/v1` requests from a synthetic farm (`farm.ts`, `api.ts`), the SignalR client
is replaced by an in-memory hub (`signalr.ts`), and `simulator.ts` advances prints,
finishes them, and sends the next matching plate when a printer is marked ready.
Production builds contain none of it; CI checks that.

UI changes reach the demo on the next site build. When an API response changes
shape, the typed demo data fails the web build where it needs an update. A page
whose data the demo does not serve logs `[demo] unhandled GET` in the browser
console of `./dev.sh demo`. Unsupported actions answer "Not available in the demo."

The page talks to the demo with `postMessage` (`src/scripts/live-demo.ts`): tour
chips and "Show in demo" buttons navigate it and ring the control to try, the
active chip follows the demo's route, its clock pauses while it is off screen,
and Reset reloads it. The demo renders at 90% on wide screens, like browser zoom,
so the desktop layout fits the window; phones get the app's own mobile layout.
Part thumbnails come from `../tools/demo-thumbnails.py` plus two real slicer
renders from our test fixtures. `/app-preview/` is `noindex` and not in the sitemap.

When the demo window first comes into view, `src/scripts/nozzle-trace.ts` prints
its frame: a white-hot nozzle drops in and draws two perimeters around the window,
cooling from white to amber to cyan (Canvas 2D; the loop stops when it is done).
Reset prints it again. Printer detail in the demo shows real P1S and U1 camera
stills (`../web/src/demo/assets/camera-*.jpg`, the U1 label pixelated).

Reduced motion turns off the headline's print animation, the nozzle trace (only
its finished frame is drawn), the flow pulse, and the demo's highlight rings.

## Search and link previews

`src/layouts/Site.astro` contains canonical, social, and homepage structured-data
metadata. The root `robots.txt` advertises `/sitemap.xml`, which indexes both
`/sitemap-pages.xml` and Docusaurus's `/docs/sitemap.xml`. Add future marketing
page URLs to `src/pages/sitemap-pages.xml.ts`; omit error pages.

`public/social-card.png` is the 1200 × 630 link-preview image. Its editable source
is `public/social-card.svg`; export that SVG to PNG after changing it. Neither
file is an application screenshot. The 404 page is marked `noindex`.

After publishing, verify these public endpoints and submit `/sitemap.xml` in a
Search Console URL-prefix property for `https://getopenfarm.github.io/`. Search
performance and indexing can only be assessed after deployment.

## Build and preview both sites

From the repository root:

```bash
(cd website && bun install --frozen-lockfile)
(cd docs && bun install --frozen-lockfile)
(cd web && bun install --frozen-lockfile)
bash tools/build-site.sh
python3 -m http.server 8080 --directory site-dist
```

Open http://localhost:8080/ for the website and http://localhost:8080/docs/ for
the guides. The build typechecks both projects, validates the docs' internal links,
generates local search, and checks local HTML links and asset references across
the assembled site. It builds the live demo and checks homepage metadata and structured-data JSON,
robots.txt, and sitemap URLs. Python 3 is needed for this check and preview server.

`site-dist/` contains Astro at its root, the complete Docusaurus build under
`docs/`, and the demo build under `app-preview/`. It is generated and ignored by
Git. Every build must succeed before the previous combined output is replaced.
Astro owns `/`; Docusaurus owns `/docs/`; the demo owns `/app-preview/`. Use
ordinary links between them and keep both prefixes out of Astro routes.

Before publishing, review 375px, 768px, and 1440px widths, 200% zoom, reduced
motion, keyboard focus into and out of the demo, and documentation search. Browser review remains a
user check under the repository UI conventions.

## One-time GitHub setup

1. Create a **public** repository named `getopenfarm/getopenfarm.github.io`.
   Initialize its `main` branch with a README so CI can check it out. Treat this
   repository as generated output: publishing replaces its tracked contents.
   Keep source, hand-written files, and workflows in `openfarm`.
2. In that repository, open **Settings → Pages**. Choose **Deploy from a branch**,
   branch **main**, folder **/ (root)**, and save. The build adds `.nojekyll` so
   GitHub serves the generated files without Jekyll processing.
3. Create a fine-grained personal access token with resource owner `getopenfarm`,
   repository access limited to `getopenfarm.github.io`, and **Contents: Read
   and write**. Complete organization approval if required. No workflow write
   permission is needed. Give it an expiration and rotate it before expiry.
4. In the **source** repository `getopenfarm/openfarm`, add an Actions secret
   named **PAGES_DEPLOY_TOKEN** containing that token. Never commit the token.
5. Merge the source changes into `main`, or run **Website and documentation**
   manually from `main` in the Actions tab.

The existing `.github/workflows/docs.yml` builds both projects on relevant pull
requests and uploads the combined artifact for review. Only a successful build
on `main` publishes it. The publish job commits generated output to the target
repository; that push uses the scoped token and triggers its branch-based Pages
deployment. The default source `GITHUB_TOKEN` cannot write to that other repo.
Publishing fails with a setup message when `PAGES_DEPLOY_TOKEN` is absent.

The final URLs are:

- https://getopenfarm.github.io/ — Astro
- https://getopenfarm.github.io/docs/ — Docusaurus

Allow the destination repository's Pages deployment to finish after the source
workflow succeeds. Check a nested guide directly, refresh it, and try docs search.
Main-branch publish runs are serialized; pull request builds never receive the
publishing credential. Weekly dependency auditing includes both sites.

To change the hostname later, update Astro's `site`, the docs `url`/`baseUrl`,
the Website navigation link, the social-card artwork, and the origin in
`tools/check-site.py` together. Astro's sitemap and robots endpoints derive
absolute URLs from `site`.
