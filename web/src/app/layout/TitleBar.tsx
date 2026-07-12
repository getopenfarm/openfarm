import { useMatches } from 'react-router';
import { ThemeToggle } from '@/components/theme-toggle';
import { SidebarTrigger } from '@/components/ui/sidebar';
import { FarmPulse } from './FarmPulse';

interface RouteHandle {
  title?: string;
}

function usePageTitle(): string {
  const matches = useMatches() as Array<{ handle?: unknown }>;
  for (let i = matches.length - 1; i >= 0; i--) {
    const handle = matches[i].handle as RouteHandle | undefined;
    if (handle?.title) return handle.title;
  }
  return 'OpenFarm';
}

export function TitleBar() {
  const title = usePageTitle();

  return (
    <header className="flex h-12 items-center gap-2 border-b px-4">
      <SidebarTrigger />
      <h1 className="font-heading text-sm font-semibold">{title}</h1>
      <div className="ml-auto flex items-center gap-2">
        <FarmPulse />
        <ThemeToggle />
      </div>
    </header>
  );
}
