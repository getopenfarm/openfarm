---
title: Cost catalog
description: Reuse estimated hardware, packaging, consumable, labor, and shipping costs across assemblies.
---

The **Cost catalog** stores prices for materials and services that you use across products. It supports cost estimates. It does not track stock or purchases.

## Add an item

Open **Cost catalog → Add cost item**. Select the type, category, and unit. Then enter the pricing details. Physical items can be counted or measured. Service rates can apply per product or by time. OpenFarm converts a package price to a price per unit.

For example, a roll priced at 40 for 100 m × 100 cm contains 100 m² and costs 0.40/m². A 50 cm × 20 cm cut uses 0.10 m², costing 0.04 before any extra waste allowance.

Leave a price blank if it is unknown. Enter zero only when the known price is zero. Quantities and dimensions must be positive. Piece counts must be whole numbers. You cannot change an item's unit if an assembly already uses it.

## Apply items to an assembly

In the assembly's **Cost items** section, select an item and enter its use per finished product. Hourly services take **minutes per product**. For area items, choose direct area or cut dimensions and piece count.

Choose what the item **Counts as**:

- **Production** includes the cost in parent assemblies, multiplied by the required quantity.
- **Final packing and delivery** includes the cost only when this assembly is the finished product. It excludes that cost from parent assemblies.

Packaging and shipping default to final packing and delivery. Check that choice for the actual product.

## Interpret the Build estimate

The assembly's Build panel shows category subtotals, product cost, shipping allowance, and the total including shipping. Missing inputs leave costs partial or unavailable. Area calculations assume you can reuse offcuts. OpenFarm does not calculate an optimized cutting layout.

For a standalone part with labor or packaging, use a one-part assembly. Continue with [Pricing and fees](pricing.md).
