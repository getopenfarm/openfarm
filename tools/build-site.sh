#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

# Dependencies are installed separately with frozen lockfiles, locally or in CI.
(cd website && bun run build)
(cd docs && bun run typecheck && bun run build)

# Both builds must succeed before replacing the combined output.
rm -rf site-dist
mkdir -p site-dist/docs
cp -a website/dist/. site-dist/
cp -a docs/build/. site-dist/docs/
touch site-dist/.nojekyll
python3 tools/check-site.py
