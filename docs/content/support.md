---
title: Support and contributing
description: Report a reproducible problem or improve OpenFarm's guides.
---

OpenFarm is developed in the [getopenfarm/openfarm repository](https://github.com/getopenfarm/openfarm). Use its [issue tracker](https://github.com/getopenfarm/openfarm/issues) for bugs and feature requests.

## Report a problem

Include the application version/image tag, installation method, affected printer model/firmware, steps to reproduce, expected result, and actual error. For a job problem, include its matching diagnosis and whether it was direct, run-based, or backfill work.

Provide a small sample file only if you can share it. Remove access codes, API tokens, signing keys, passwords, and personal data from logs/configuration.

## Improve a guide

Use **Edit this page** to propose a documentation change. Guides live in `docs/content/`; navigation order lives in `docs/sidebars.ts`. Follow the [local docs workflow](development/local-setup.md#run-the-documentation) and build before opening a pull request.

Describe the current product behavior and use the UI's actual action names. Keep future ideas and implementation notes in `dev-notes/` so operators can follow the guides without mistaking planned work for a shipped feature.
