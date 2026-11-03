---
title: Assemblies
description: Define products from printed parts and nested assemblies, with separate additional costs.
---

An assembly is a reusable product definition. It contains at least one part or sub-assembly with quantities. Creating one does not create print jobs.

## Define the structure

1. Open **Assemblies → Add assembly**.
2. Set the name and description.
3. In **Structure**, use **Add part or assembly** and set quantities per finished assembly.
4. Add additional [Cost items](../costs/catalog.md) such as hardware, packaging, labor, or shipping as needed.
5. Save the assembly.

For nested assemblies, OpenFarm multiplies component quantities through each level. It prevents circular definitions, such as an assembly containing itself. The library selection list can find items across folders. Moving a record to another folder does not change its bill of materials (BOM) references.

## Plan production

For complete products, create a [run](../production/runs.md) and request the assembly in **sets**. OpenFarm expands the whole batch and totals each printed part before splitting it into plate jobs. It does not multiply the rounded plate count for one set by the number of sets.

The assembly's direct **Print** action lets you enter whole-plate counts for each printed component. Use a run's set quantity when you need an exact number of complete products.

Non-printed cost items contribute estimated costs, not printer output or stock reservations. A run does not purchase or consume cost-catalog inventory.

## Cost and pricing

The assembly page's Build estimate combines printed materials, configured printer costs, additional costs, and applicable allowances. The Pricing section calculates sale-price estimates against fee profiles. See [Costs and pricing](../costs/pricing.md).

To include packaging or labor for a standalone part, create an assembly containing that part. If inputs are missing, the cost estimate remains partial or unknown.
