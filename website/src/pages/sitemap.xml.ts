import type { APIRoute } from 'astro';

// Keep the website and Docusaurus sitemaps discoverable under one index.
export const GET: APIRoute = () => new Response(
  `<?xml version="1.0" encoding="UTF-8"?>
<sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
  <sitemap><loc>${new URL('/sitemap-pages.xml', import.meta.env.SITE).href}</loc></sitemap>
  <sitemap><loc>${new URL('/docs/sitemap.xml', import.meta.env.SITE).href}</loc></sitemap>
</sitemapindex>`,
  { headers: { 'Content-Type': 'application/xml; charset=utf-8' } },
);
