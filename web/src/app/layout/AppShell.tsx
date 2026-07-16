import { Outlet } from 'react-router';
import { SidebarInset, SidebarProvider } from '@/components/ui/sidebar';
import { TooltipProvider } from '@/components/ui/tooltip';
import { useFarmHub } from '@/features/printers/hooks/useFarmHub';
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

  const cookieState = readSidebarCookie();
  const defaultOpen = cookieState ?? true;

  return (
    <TooltipProvider>
      <SidebarProvider defaultOpen={defaultOpen}>
        <AppSidebar />
        <SidebarInset>
          <TitleBar />
          <div className="flex-1 p-4">
            <Outlet />
          </div>
        </SidebarInset>
      </SidebarProvider>
    </TooltipProvider>
  );
}
