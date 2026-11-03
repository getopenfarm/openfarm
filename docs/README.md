# OpenFarm documentation

Independent Docusaurus 3 site. User guides live in `content/`; engineering notes
and plans live in [`../dev-notes/`](../dev-notes/).

## Develop

From the repository root, run `./dev.sh docs`. It installs the frozen docs
dependencies and starts the development server on http://localhost:3000.
Additional Docusaurus options pass through, for example
`./dev.sh docs --port 3001 --no-open`.

Use Node.js 20+ and Bun 1.3.14. From this directory:

```bash
bun install --frozen-lockfile
bun run start
```

The development server defaults to http://localhost:3000.

## Validate and preview production

```bash
bun run typecheck
bun run build
bun run audit
bun run serve
```

The build fails on broken internal links or Markdown links and generates the
local search index. Search is available in the production preview; a fresh dev
server does not generate an index. There is no hosted search account or API key.

Check narrow screens, light/dark mode, 200% zoom, keyboard navigation, search,
sidebar, and focus appearance during visual review. A successful build does not
replace that review.

The audit uses narrowly documented tooling exceptions in
[`dev-notes/DOCS_DEPENDENCY_AUDIT.md`](../dev-notes/DOCS_DEPENDENCY_AUDIT.md).

## Content and navigation

- Edit `content/` for user documentation and `sidebars.ts` for reading order.
- The quick start is the site root. There is no separate marketing homepage/blog.
- Add titles and descriptions to each guide and link using relative `.md` paths.
- Verify workflow names and constraints against the current frontend, domain,
  and endpoint code. See [the source map](../dev-notes/DOCUMENTATION.md).
- Keep future plans, architecture notes, and protocol details in `dev-notes/`.
- `src/css/custom.css` follows the app's Geist typography, zinc surfaces, blue
  accent, rounded corners, and theme behavior from `web/src/index.css`.
- `static/img/openfarm.svg` is the existing app mark from `web/public/favicon.svg`.

## Static hosting

Publish the **contents** of `build/` with any static host. This repository change
does not deploy the site or put the guides in the application image.

For a dedicated hostname:

```bash
DOCS_URL=https://docs.example.com bun run build
```

For a project subpath, set the origin and a leading/trailing-slash base path:

```bash
DOCS_URL=https://getopenfarm.github.io DOCS_BASE_URL=/openfarm/ bun run build
```

Both settings are build-time values. The default origin is
`https://getopenfarm.github.io`, with `/` as the base path; change it to the actual
host before publishing so canonical and sitemap URLs are correct. Links, assets,
and local search support the configured base path. The generated site is static:
no backend, database, login, or printer connection is needed to serve it.
