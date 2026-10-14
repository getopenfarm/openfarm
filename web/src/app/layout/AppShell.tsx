import { Outlet, useMatches } from 'react-router';
import { SidebarInset, SidebarProvider } from '@/components/ui/sidebar';
import { TooltipProvider } from '@/components/ui/tooltip';
import { useFarmHub } from '@/features/printers/hooks/useFarmHub';
import { cn } from '@/lib/utils';
import { AppSidebar } from './AppSidebar';
import { TitleBar } from './TitleBar';

function readSidebarCookie(): boolean | null {
  if (typeof document === 'undefined') return null;
  const match = document.cookie.match(/(?:^|;\s*)sidebar_state=(true|false)/);
  if (!match) return null;
  return match[1] === 'true';
}

export function AppShell() {
  useFarmHub();
  const fillViewport = useMatches().some(
    (match) =>
      (match.handle as { fillViewport?: boolean } | undefined)?.fillViewport,
  );

  const cookieState = readSidebarCookie();
  const defaultOpen = cookieState ?? true;

  return (
    <TooltipProvider>
      <SidebarProvider
        defaultOpen={defaultOpen}
        className={fillViewport ? 'h-dvh min-h-0' : undefined}
      >
        <AppSidebar />
        <SidebarInset
          className={cn('min-w-0', fillViewport && 'min-h-0 overflow-hidden')}
        >
          <TitleBar />
          <div
            className={
              fillViewport
                ? 'flex min-h-0 flex-1 flex-col overflow-y-auto p-4'
                : 'flex flex-1 flex-col p-4'
            }
          >
            <Outlet />
          </div>
        </SidebarInset>
      </SidebarProvider>
    </TooltipProvider>
  );
}
