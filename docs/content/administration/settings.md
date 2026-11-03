---
title: Farm settings
description: Configure currency, cost inputs, fee profiles, and the default slicer.
---

Open **Settings** from the user menu. Your permissions determine which sections you can open and whether you can save changes.

## General

Set the farm currency, electricity rate, and failure allowance for estimates. The currency setting labels the farm's monetary values. It does not convert amounts between currencies. If you change the currency, review the prices already entered.

Failure allowance ranges from zero to below 100% and defaults to zero. It increases estimated filament, depreciation, and electricity costs to allow for failed prints. It does not increase every product cost.

## Printer costs

Configure the purchase cost, depreciation settings, and average power for each printer model. OpenFarm combines these values with estimated print time and the electricity rate. The assembly's Build panel shows the resulting costs.

Unknown values remain unknown. A model rate does not guarantee every part has a usable time estimate.

## Fees

Create a fee profile for each sales channel. Enter its percentage fees and fixed fees. Every assembly's pricing estimate shows every fee profile. See [Pricing and fees](../costs/pricing.md).

## Slicing defaults

Choose an installed slicer and its exact version. This sets the profile catalog shown in selection lists and the default for new file processing. Existing parts keep their saved slicer and version.

Estimates use supported file metadata or project presets. There is no separate settings page for farm estimation profiles. Enter connection credentials when you add or edit a printer.

For host environment variables, networking, database, and public-origin configuration, see [Installation configuration](../installation/configuration.md).
