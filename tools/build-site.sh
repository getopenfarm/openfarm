#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

# Dependencies are installed separately with frozen lockfiles, locally or in CI.
(cd website && bun run build && bun run typecheck)
(cd docs && bun run typecheck && bun run build)
# The live demo is the real app on in-browser demo data (web/src/demo), embedded at /app-preview/.
(cd web && bun run build:demo)

# Every build must succeed before replacing the combined output.
rm -rf site-dist
mkdir -p site-dist/docs site-dist/app-preview
cp -a website/dist/. site-dist/
cp -a docs/build/. site-dist/docs/
cp -a web/dist-demo/. site-dist/app-preview/
touch site-dist/.nojekyll
python3 tools/check-site.py
