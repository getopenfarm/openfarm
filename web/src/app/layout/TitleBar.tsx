import { Fragment } from 'react';
import { Link } from 'react-router';
import { ThemeToggle } from '@/components/theme-toggle';
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from '@/components/ui/breadcrumb';
import { Separator } from '@/components/ui/separator';
import { SidebarTrigger } from '@/components/ui/sidebar';
import { FarmPulse } from './FarmPulse';
import { useBreadcrumb } from './useBreadcrumb';

export function TitleBar() {
  const trail = useBreadcrumb();

  return (
    <header className="flex h-11 shrink-0 items-center gap-3 border-b px-4">
      <SidebarTrigger />
      <Separator orientation="vertical" />
      <Breadcrumb>
        <BreadcrumbList className="flex-nowrap text-sm">
          {trail.map((entry, index) => {
            const isLast = index === trail.length - 1;
            return (
              <Fragment key={`${entry.title}-${entry.url ?? index}`}>
                <BreadcrumbItem
                  className={isLast ? undefined : 'text-muted-foreground'}
                >
                  {isLast ? (
                    <BreadcrumbPage className="font-heading font-semibold text-foreground">
                      {entry.title}
                    </BreadcrumbPage>
                  ) : entry.url ? (
                    <BreadcrumbLink asChild>
                      <Link to={entry.url}>{entry.title}</Link>
                    </BreadcrumbLink>
                  ) : (
                    entry.title
                  )}
                </BreadcrumbItem>
                {isLast ? null : <BreadcrumbSeparator />}
              </Fragment>
            );
          })}
        </BreadcrumbList>
      </Breadcrumb>
      <div className="ml-auto flex items-center gap-3">
        <FarmPulse />
        <Separator orientation="vertical" />
        <ThemeToggle />
      </div>
    </header>
  );
}
