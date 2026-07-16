import { useMatches } from 'react-router';
import { navSections } from './nav-config';

interface RouteHandle {
  title?: string;
}

function useLeafTitle(): string {
  const matches = useMatches() as Array<{ handle?: unknown }>;
  for (let i = matches.length - 1; i >= 0; i--) {
    const handle = matches[i].handle as RouteHandle | undefined;
    if (handle?.title) return handle.title;
  }
  return 'OpenFarm';
}

export interface Breadcrumb {
  root: string | null;
  leaf: string;
}

/** Finds which sidebar section the current route belongs to (path-prefix match, so nested
 * routes like /parts/new still resolve to their parent nav item's section) and pairs it with
 * the route's own title for the header breadcrumb. */
export function useBreadcrumb(): Breadcrumb {
  const matches = useMatches();
  const pathname = matches[matches.length - 1]?.pathname ?? '/';
  const leaf = useLeafTitle();

  const section = navSections.find((s) =>
    s.items.some((item) =>
      item.url === '/' ? pathname === '/' : pathname.startsWith(item.url),
    ),
  );

  return { root: section?.label ?? null, leaf };
}
