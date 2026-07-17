import { useMatches } from 'react-router';
import { navSections } from './nav-config';

interface RouteHandle {
  title?: string;
}

export interface BreadcrumbEntry {
  title: string;
  /** Navigable path, or null for the current page / a non-navigable section label. */
  url: string | null;
}

/** Builds the header breadcrumb trail: the sidebar section this route belongs to (a
 * non-navigable label), followed by every ancestor route's own title (as a link to that
 * route), ending with the current route's title (not a link). */
export function useBreadcrumb(): BreadcrumbEntry[] {
  const matches = useMatches() as Array<{
    pathname: string;
    handle?: unknown;
  }>;
  const pathname = matches[matches.length - 1]?.pathname ?? '/';

  const section = navSections.find((s) =>
    s.items.some((item) =>
      item.url === '/' ? pathname === '/' : pathname.startsWith(item.url),
    ),
  );

  const entries: BreadcrumbEntry[] = section
    ? [{ title: section.label, url: null }]
    : [];

  for (const match of matches) {
    const title = (match.handle as RouteHandle | undefined)?.title;
    if (title) entries.push({ title, url: match.pathname });
  }

  if (entries.length === 0) entries.push({ title: 'OpenFarm', url: null });

  const last = entries[entries.length - 1];
  entries[entries.length - 1] = { ...last, url: null };

  return entries;
}
