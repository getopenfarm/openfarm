# Website content and SEO review

Reviewed 3 November 2026. This is an editorial review of public page structure,
messaging, and product presentation, not a measured conversion or ranking study.
The reference sites' claims are their own; they are not evidence about OpenFarm.

## Reference sites and useful patterns

| Reference | Observed presentation | Application to OpenFarm |
| --- | --- | --- |
| [Printago homepage](https://printago.io/) | Explicit product category, early dashboard image, outcome-led introduction, deeper feature sections, compatibility links, FAQ, repeated signup action. | State the category in the H1. Explain production workflows before the supporting feature grid. Use installation as the primary action. |
| [SimplyPrint print-farm page](https://simplyprint.io/print-farms) | Early views of printer management, queue, and maintenance; feature-specific links; customer evidence; compatibility and setup resources. | Make each section easy to scan and link to its guide. Prioritize real OpenFarm screen captures for the next visual pass. |
| [3DQue homepage](https://www.3dque.com/) | Production outcomes followed by queue and dashboard explanations, software visuals, and repeated demo actions. | Explain how work moves through OpenFarm and show what its matching rules do. Use concrete mechanics rather than unsupported productivity numbers. |

These are editorial choices inferred from the references. They do not establish
that a particular layout converts better. OpenFarm keeps its own visual system,
copy, and assets. The references also offer overlapping deployment and workflow
capabilities; self-hosting is a positioning choice, not an exclusivity claim.

## Gaps addressed

The first homepage described a workspace but did not name 3D print farm
management in its main heading. Six similarly sized cards gave little space to
the production workflows. Visitors had to leave the page to understand model
compatibility, installation requirements, or the physical bed-clearance step.

The revised page has this reading order:

1. Product category, self-hosted deployment, and an installation action.
2. Supported printer families and a compatibility link.
3. Production runs, printer matching, and continuous backfill.
4. Supporting features: fleet, filament, library, costs, statistics, and access.
5. Exact supported models and the deployment requirements.
6. Practical FAQ, followed by a final installation action.

The assembly quantity panel is explicitly an example. The matching panel
explains requirements; neither is presented as an application screenshot.
No customer logos, testimonials, savings percentages, uptime numbers, or fleet
size claims are available to substantiate, so the page uses verifiable product
behavior as its evidence.

## Feature evidence

| Homepage topic | Source in this repository |
| --- | --- |
| Fixed quantities, complete sets, shortfalls | `docs/content/production/runs.md` |
| Eligibility and diagnosis | `docs/content/production/matching.md` |
| Whole-plate repetition and assembly balancing | `docs/content/production/backfill.md` |
| Reusable products and nested components | `docs/content/library/assemblies.md` |
| Spool inventory and feed slots | `docs/content/filament/spools.md` |
| Estimated costs and sale prices | `docs/content/costs/pricing.md` |
| Users, groups, scoped API tokens | `docs/content/administration/accounts.md`, `docs/content/development/api.md` |
| Exact models and camera exceptions | `docs/content/printers/supported-printers.md` |
| Linux/amd64, Docker, PostgreSQL, LAN requirements | `docs/content/installation/docker.md` |
| Source license | `LICENSE` (AGPL version 3) |

Cost calculations are described as estimates. Queueing is not described as
physical bed clearing or unattended operation. The page does not imply support
for printer models outside the current catalog or introduce ecommerce, cloud
slicing, or AI failure-detection promises.

## SEO changes

- Descriptive title, H1, and description using the product's natural category.
- Server-rendered text, semantic sections, descriptive guide links, and FAQ
  answers delivered in HTML with native disclosure controls.
- Canonical homepage URL, Open Graph and Twitter metadata, and an original
  1200 × 630 branded PNG for link previews (editable SVG source included).
- Homepage `WebSite` and `SoftwareApplication` JSON-LD describing visible
  product capabilities, with no invented offers or aggregate ratings.
- Root `robots.txt` advertising `/sitemap.xml`. That sitemap index references
  the marketing-page sitemap and Docusaurus's existing `/docs/sitemap.xml`.
- Website 404 marked `noindex`, omitted from the sitemap, and without a
  canonical that could suggest a valid content page.
- Build checks for metadata, parseable JSON-LD, local assets, crawlable sitemap
  URLs, and the combined sitemap reference.

These changes follow [Google Search Essentials](https://developers.google.com/search/docs/essentials),
the [SEO starter guide](https://developers.google.com/search/docs/fundamentals/seo-starter-guide),
[sitemap guidance](https://developers.google.com/search/docs/crawling-indexing/sitemaps/build-sitemap),
and [structured data policies](https://developers.google.com/search/docs/appearance/structured-data/sd-policies).
They improve clarity and discovery; they do not establish ranking changes or
rich-result eligibility. FAQ content serves visitors without promising a
special search appearance.

## Highest-value remaining work

1. Capture real OpenFarm screens from a controlled demonstration farm: queue
   with a diagnosed waiting job, a production run with component progress,
   and filament inventory with loaded slots. Remove identifying information.
   Export appropriately sized WebP/AVIF assets with meaningful alt text and
   dimensions. Replace explanatory panels where real screens explain more.
2. Perform the repository's user visual review: narrow/medium/wide viewports,
   system light/dark themes, 200% zoom, focus, navigation wrapping, and FAQ use.
3. After publishing, verify the public homepage, nested docs, canonical URLs,
   social image, sitemap index, and error response. Register a URL-prefix
   property for `https://getopenfarm.github.io/` in Google Search Console and
   submit `/sitemap.xml`.
4. Measure real visitor questions and search queries before adding more pages.
   Create focused feature pages only when they can add useful product evidence
   beyond the existing guides. Add customer stories when consented evidence is
   available.

The app screenshots and browser review are not claimed complete. The existing
GitHub Pages destination and publishing credential setup also remain required.
