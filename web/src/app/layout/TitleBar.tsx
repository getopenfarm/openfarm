import { Fragment } from 'react';
import { Link } from 'react-router';
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
import { NotificationsBell } from '@/features/notifications/components/NotificationsBell';
import { BreadcrumbOutlet } from './BreadcrumbOutlet';
import { FarmPulse } from './FarmPulse';
import { useBreadcrumb } from './useBreadcrumb';

export function TitleBar() {
  const trail = useBreadcrumb();

  return (
    <header className="flex h-11 shrink-0 items-center gap-3 border-b px-4">
      <SidebarTrigger className="md:hidden" />
      <Separator orientation="vertical" className="md:hidden" />
      <BreadcrumbOutlet
        fallback={
          <Breadcrumb className="min-w-0 flex-1">
            <BreadcrumbList className="flex-nowrap overflow-hidden">
              {trail.map((entry, index) => {
                const isLast = index === trail.length - 1;
                return (
                  <Fragment key={`${entry.title}-${entry.url ?? index}`}>
                    <BreadcrumbItem
                      className={
                        isLast ? 'min-w-0' : 'hidden shrink-0 sm:inline-flex'
                      }
                    >
                      {isLast ? (
                        <BreadcrumbPage className="truncate">
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
                    {isLast ? null : (
                      <BreadcrumbSeparator className="hidden sm:block" />
                    )}
                  </Fragment>
                );
              })}
            </BreadcrumbList>
          </Breadcrumb>
        }
      />
      <div className="ml-auto flex shrink-0 items-center gap-3">
        <FarmPulse />
        <Separator orientation="vertical" />
        <NotificationsBell />
      </div>
    </header>
  );
}
