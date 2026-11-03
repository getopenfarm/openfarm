---
title: Pricing and fees
description: Estimate product cost, sale price, fees, and margin from current configuration.
---

An assembly's **Pricing** section calculates the expected result of a sale for every configured fee profile. It does not record actual orders, payments, or revenue.

## Complete estimated costs

- Enter material/color prices and known spool purchase prices.
- Configure model costs and average power in **Settings → Printer costs**.
- Set currency, electricity rate, and optional failure allowance in **Settings → General**.
- Add product-specific [cost items](catalog.md) to the assembly.

Printer depreciation and electricity appear as separate Build lines when the needed model/rate/time inputs exist. A missing estimate or rate must remain visible as partial or unavailable.

The failure allowance applies to filament, depreciation, and electricity. It does not increase packaging, labor, other cost items, or shipping.

## Set fee profiles

Open **Settings → Fees** and add a profile for each sales channel. Each fee is a **% of sale price** or **Fixed per order** amount. Combined percent fees must stay below 100%.

Allocate a monthly fee as a per-order fixed amount yourself, for example 48/month divided by 400 orders = 0.12/order. The fee preview excludes production costs. Current assembly calculations count fixed fees once per unit, as though one order contains one unit.

## Choose the pricing rule

- **Price** keeps the sale amount fixed as costs change.
- **Margin** targets profit after fees as a share of the price. Price can differ by fee profile.
- **Markup** adds a percentage to cost before fees. Price is the same across fee profiles.

A blank rule shows break-even prices. The table shows fees, profit, margin, markup, and maximum discount before loss. An impossible margin target shows no calculated price with an explanation.

Costs and fees are current estimates. A partial cost can overstate expected profit; complete missing inputs before relying on a quote.
