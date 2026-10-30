import { FolderIcon } from 'lucide-react';
import { Fragment, type ReactNode } from 'react';
import { Link } from 'react-router';
import { PageBreadcrumb } from '@/app/layout/BreadcrumbOutlet';
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from '@/components/ui/breadcrumb';
import { Button } from '@/components/ui/button';
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip';
import { cn } from '@/lib/utils';
import { type Folder, type LibraryKind, useFolders } from './api';
import { LibraryBreadcrumbTarget } from './LibraryDnd';

export function LibraryBreadcrumb(props: Parameters<typeof LibraryTrail>[0]) {
  return (
    <PageBreadcrumb>
      <LibraryTrail {...props} />
    </PageBreadcrumb>
  );
}

export function LibraryTrail({
  library,
  sectionLabel = true,
  ancestors,
  currentLabel,
  disabled = false,
  dropTargets = false,
}: {
  sectionLabel?: boolean;
  library: LibraryKind;
  ancestors: Folder[];
  currentLabel?: string;
  disabled?: boolean;
  dropTargets?: boolean;
}) {
  const locations = [
    { id: null, name: library === 'parts' ? 'Parts' : 'Assemblies' },
    ...ancestors,
  ];
  return (
    <Breadcrumb
      aria-label={sectionLabel ? 'breadcrumb' : 'Folder navigation'}
      className="min-w-0 flex-1 overflow-x-auto"
    >
      <BreadcrumbList
        className={cn(
          'w-max min-w-full flex-nowrap px-1',
          sectionLabel && 'py-1',
        )}
      >
        {sectionLabel && (
          <BreadcrumbItem className="shrink-0">Library</BreadcrumbItem>
        )}
        {locations.map((folder, index) => {
          const last = index === locations.length - 1 && !currentLabel;
          const iconRoot = !sectionLabel && folder.id === null;
          const rootLabel =
            library === 'parts' ? 'Part library root' : 'Assembly library root';
          const label = iconRoot ? (
            <FolderIcon aria-hidden="true" />
          ) : (
            folder.name
          );
          let content: ReactNode = last ? (
            <BreadcrumbPage
              className="max-w-48 truncate"
              title={iconRoot ? undefined : folder.name}
              aria-label={iconRoot ? rootLabel : undefined}
              tabIndex={iconRoot ? 0 : undefined}
            >
              {label}
            </BreadcrumbPage>
          ) : (
            <BreadcrumbLink asChild>
              <Link
                draggable={false}
                to={
                  folder.id
                    ? `/${library}?folder=${encodeURIComponent(folder.id)}`
                    : `/${library}`
                }
                className="max-w-48 truncate"
                title={iconRoot ? undefined : folder.name}
                aria-label={iconRoot ? rootLabel : undefined}
                aria-disabled={disabled || undefined}
                onClick={(event) => {
                  if (disabled) event.preventDefault();
                }}
              >
                {label}
              </Link>
            </BreadcrumbLink>
          );
          if (iconRoot) {
            content = (
              <TooltipProvider>
                <Tooltip>
                  <TooltipTrigger asChild>
                    <Button
                      asChild
                      variant="ghost"
                      size="icon"
                      className="focus-visible:ring-inset"
                    >
                      {content}
                    </Button>
                  </TooltipTrigger>
                  <TooltipContent>{rootLabel}</TooltipContent>
                </Tooltip>
              </TooltipProvider>
            );
          }
          return (
            <Fragment key={folder.id ?? 'root'}>
              {(sectionLabel || index > 0) && <BreadcrumbSeparator />}
              <BreadcrumbItem className="min-w-0 shrink-0">
                {dropTargets ? (
                  <LibraryBreadcrumbTarget
                    folderId={folder.id}
                    name={folder.name}
                    icon={iconRoot}
                  >
                    {content}
                  </LibraryBreadcrumbTarget>
                ) : (
                  content
                )}
              </BreadcrumbItem>
            </Fragment>
          );
        })}
        {currentLabel && (
          <>
            <BreadcrumbSeparator />
            <BreadcrumbItem className="min-w-0 shrink-0">
              <BreadcrumbPage
                className="max-w-64 truncate"
                title={currentLabel}
              >
                {currentLabel}
              </BreadcrumbPage>
            </BreadcrumbItem>
          </>
        )}
      </BreadcrumbList>
    </Breadcrumb>
  );
}

/** Detail pages pass saved membership; Add pages pass their destination folder. */
export function LibraryItemBreadcrumb({
  library,
  folderId,
  name,
}: {
  library: LibraryKind;
  folderId?: string | null;
  name: string;
}) {
  const folders = useFolders(library, folderId ?? null, 1, 1, !!folderId);
  return (
    <LibraryBreadcrumb
      library={library}
      ancestors={folders.data?.ancestors ?? []}
      currentLabel={name}
    />
  );
}
