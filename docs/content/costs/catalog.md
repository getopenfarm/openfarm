---
title: Cost catalog
description: Reuse estimated hardware, packaging, consumable, labor, and shipping costs across assemblies.
---

The **Cost catalog** stores reusable physical-material and service rates. It supports product estimates; it is not an inventory or purchasing ledger.

## Add an item

Open **Cost catalog → Add cost item**. Choose its type/category, unit, and pricing configuration. Physical items can be counted or measured; service rates can apply per product or by time. Package pricing is converted to an effective unit rate.

For example, a roll priced at 40 for 100 m × 100 cm contains 100 m² and costs 0.40/m². A 50 cm × 20 cm cut uses 0.10 m², costing 0.04 before any extra waste allowance.

Blank prices are unknown and zero is a valid known price. Quantities and dimensions must be positive; pieces use whole counts. An item already referenced by an assembly cannot change unit.

## Apply items to an assembly

In the assembly's **Cost items** section, select an item and enter its use per finished product. Hourly services take **minutes per product**. For area items, choose direct area or cut dimensions and piece count.

Choose what the item **Counts as**:

- **Production** carries through nested assemblies with the required quantity.
- **Final packing and delivery** applies only when this assembly is the finished product being estimated; it does not carry into a parent's product costs.

Packaging and shipping default to final packing/delivery. Check that choice for the actual product.

## Interpret the Build estimate

The assembly's Build panel separates category subtotals, product cost, shipping allowance, and the total including shipping. Missing inputs leave costs partial or unavailable. Area calculations assume reusable offcuts; they do not optimize cutting layouts.

For a standalone part with labor or packaging, use a one-part assembly. Continue with [Pricing and fees](pricing.md).
