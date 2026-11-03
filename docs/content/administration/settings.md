---
title: Farm settings
description: Configure currency, cost inputs, fee profiles, and the default slicer.
---

Open **Settings** from the user menu. The available sections depend on your permissions; view access does not necessarily include save access.

## General

Set the farm currency, electricity rate, and failure allowance for estimates. Currency is a common label for the farm's values, not an exchange-rate conversion service. Review existing entered prices when changing it.

Failure allowance ranges from zero to below 100% and defaults to zero. It adds estimated waste exposure to filament, depreciation, and electricity, not all product costs.

## Printer costs

Configure costs by printer model, including purchase/depreciation inputs and average power. These combine with estimated print time and the electricity rate to populate assembly Build lines.

Unknown values remain unknown. A model rate does not guarantee every part has a usable time estimate.

## Fees

Create fee profiles for sales channels and edit their percentage/fixed fees. Every assembly's pricing estimate shows every fee profile. See [Pricing and fees](../costs/pricing.md).

## Slicing defaults

Choose a bundled engine and exact version. This selects the catalog for profile pickers and default processing choices. It does not silently repin existing parts.

Estimation settings come from supported file metadata/project presets; there is no separate farm estimation-profile panel. Credentials are entered on the printer's add/edit page, not in a standalone credential registry.

For host environment variables, networking, database, and public-origin configuration, see [Installation configuration](../installation/configuration.md).
