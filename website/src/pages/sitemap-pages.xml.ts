import type { APIRoute } from 'astro';

// Add future marketing pages here; exclude the error page.
export const GET: APIRoute = () => new Response(
  `<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
  <url><loc>${new URL('/', import.meta.env.SITE).href}</loc></url>
</urlset>`,
  { headers: { 'Content-Type': 'application/xml; charset=utf-8' } },
);
