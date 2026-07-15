import { SidebarLayoutToggle } from '@/components/sidebar-layout-toggle/SidebarLayoutToggle';
import { ThemeToggle } from '@/components/theme-toggle';
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from '@/components/ui/breadcrumb';
import { Separator } from '@/components/ui/separator';
import { SidebarTrigger } from '@/components/ui/sidebar';
import { FarmPulse } from './FarmPulse';
import { useBreadcrumb } from './useBreadcrumb';

export function TitleBar() {
  const { root, leaf } = useBreadcrumb();

  return (
    <header className="flex h-[52px] shrink-0 items-center gap-3 border-b px-4">
      <SidebarTrigger />
      <Separator orientation="vertical" className="h-5" />
      <Breadcrumb>
        <BreadcrumbList className="flex-nowrap text-sm">
          {root ? (
            <>
              <BreadcrumbItem className="text-muted-foreground">
                {root}
              </BreadcrumbItem>
              <BreadcrumbSeparator />
            </>
          ) : null}
          <BreadcrumbItem>
            <BreadcrumbPage className="font-heading font-semibold text-foreground">
              {leaf}
            </BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>
      <div className="ml-auto flex items-center gap-3">
        <FarmPulse />
        <Separator orientation="vertical" className="h-5" />
        <div className="hidden items-center gap-2 md:flex">
          <span className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
            Sidebar
          </span>
          <SidebarLayoutToggle />
        </div>
        <ThemeToggle />
      </div>
    </header>
  );
}
