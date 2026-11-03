# OpenFarm website and Pages deployment

The Astro site lives here; Docusaurus stays in `../docs`. Application code,
website content, and user guides all remain in `getopenfarm/openfarm`.

## Develop

Use Node.js 24 and Bun 1.3.14. From the repository root:

```bash
./dev.sh website     # http://localhost:4321
./dev.sh docs        # http://localhost:3000/docs/ (a separate terminal)
```

The development servers are independent. Links from the homepage to `/docs/`
work in the combined production preview below.

Edit `src/pages/index.astro` for homepage copy and `src/styles/global.css` for
styles. Keep claims aligned with `docs/content/` and the application. Match the
Geist fonts, zinc surfaces, blue primary, 10px corners, and light/dark colors in
`../web/src/index.css` and `../docs/src/css/custom.css`. The website follows the
system color scheme. `public/openfarm.svg` is copied from the application mark
in `../web/public/favicon.svg`; update both when branding changes.

## Build and preview both sites

From the repository root:

```bash
(cd website && bun install --frozen-lockfile)
(cd docs && bun install --frozen-lockfile)
bash tools/build-site.sh
python3 -m http.server 8080 --directory site-dist
```

Open http://localhost:8080/ for the website and http://localhost:8080/docs/ for
the guides. The build typechecks the docs, validates their internal links,
generates local search, and checks local HTML links and asset references across
the assembled site. Python 3 is needed for this check and preview server.

`site-dist/` contains Astro at its root and the complete Docusaurus build under
`docs/`. It is generated and ignored by Git. Both builds must succeed before
the previous combined output is replaced. Astro owns `/`; Docusaurus owns
`/docs/`. Use ordinary links between them and keep `/docs/` out of Astro routes.

Before publishing, review 375px, 768px, and 1440px widths, both system themes,
200% zoom, keyboard focus, and documentation search. Browser review remains a
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
the Website navigation link, and the origin in `tools/check-site.py` together.
