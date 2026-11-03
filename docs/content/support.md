---
title: Support and contributing
description: Report a reproducible problem or improve OpenFarm's guides.
---

OpenFarm is developed in the [getopenfarm/openfarm repository](https://github.com/getopenfarm/openfarm). Use its [issue tracker](https://github.com/getopenfarm/openfarm/issues) for bugs and feature requests.

## Report a problem

Include these details:

- Application version or image tag.
- Installation method.
- Affected printer model and firmware version.
- Steps to reproduce the problem.
- Expected result and actual error.

For a job problem, include its matching diagnosis. State whether you printed directly, created a run, or used backfill.

Provide a small sample file only if you can share it. Before sharing logs or configuration, remove access codes, API tokens, signing keys, passwords, and personal data.

## Improve a guide

Use **Edit this page** to propose a documentation change. Guides live in `docs/content/`; navigation order lives in `docs/sidebars.ts`. Follow the [local docs workflow](development/local-setup.md#run-the-documentation) and build before opening a pull request.

Describe the current product behavior and use the UI's actual action names. Keep future ideas and implementation notes in `dev-notes/` so operators can follow the guides without mistaking planned work for a shipped feature.

Use short, active sentences and consistent technical terms. Explain unfamiliar OpenFarm terms and give each numbered step one main action. Keep commands, interface labels, technical limits, and behavior exceptions accurate.
