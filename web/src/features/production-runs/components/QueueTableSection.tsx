/** biome-ignore-all lint/a11y/noNoninteractiveTabindex: Each scroll viewport needs keyboard focus for independent scrolling. */
/** biome-ignore-all lint/a11y/useSemanticElements: ARIA table elements allow independently scrolling, virtualized row groups with a shared column header. */
/** biome-ignore-all lint/a11y/useFocusableInteractive: This is a static table, not an interactive grid; links and action buttons provide keyboard access. */
import { useVirtualizer } from '@tanstack/react-virtual';
import { ChevronDownIcon, ChevronRightIcon } from 'lucide-react';
import type {
  ComponentProps,
  HTMLAttributes,
  ReactNode,
  RefObject,
} from 'react';
import { useId } from 'react';
import { Badge } from '@/components/ui/badge';
import type { QueueJobRowResponse } from '@/lib/api/generated/endpoints.schemas';
import { cn } from '@/lib/utils';
import { QueuePagination } from './QueuePagination';

export const QUEUE_COLUMNS =
  'minmax(260px, 2fr) 80px minmax(220px, 1.5fr) 140px 80px';

export function QueueTableCell({ className, ...props }: ComponentProps<'div'>) {
  return <div role="cell" className={cn('p-2', className)} {...props} />;
}

interface QueueTableSectionProps {
  label: string;
  description: string;
  collapsed?: boolean;
  onToggle?: () => void;
  jobs: QueueJobRowResponse[];
  totalCount: number;
  page: number;
  pageSize: number;
  isFetching: boolean;
  isError: boolean;
  isLoading: boolean;
  scrollRef: RefObject<HTMLDivElement | null>;
  feedback: ReactNode;
  renderRow: (job: QueueJobRowResponse) => ReactNode;
  rowProps: (job: QueueJobRowResponse) => HTMLAttributes<HTMLDivElement>;
  onPageChange: (page: number) => void;
  onPageSizeChange: (size: number) => void;
}

/** Each section owns its vertical viewport; headers and pagination never scroll away. */
export function QueueTableSection({
  label,
  description,
  collapsed = false,
  onToggle,
  jobs,
  totalCount,
  page,
  pageSize,
  isFetching,
  isError,
  isLoading,
  scrollRef,
  feedback,
  renderRow,
  rowProps,
  onPageChange,
  onPageSizeChange,
}: QueueTableSectionProps) {
  const contentId = useId();
  const virtualizer = useVirtualizer({
    enabled: !collapsed,
    count: jobs.length,
    getScrollElement: () => scrollRef.current,
    getItemKey: (index) => jobs[index].jobId ?? index,
    estimateSize: () => 56,
    overscan: 8,
  });
  const heading = (
    <>
      {onToggle &&
        (collapsed ? (
          <ChevronRightIcon className="size-4" />
        ) : (
          <ChevronDownIcon className="size-4" />
        ))}
      {label}
      <Badge variant="secondary">
        {isError || isLoading ? '—' : totalCount}
      </Badge>
      <span className="text-xs font-normal text-muted-foreground">
        {description}
      </span>
    </>
  );
  return (
    <div
      role="rowgroup"
      aria-label={label}
      className="flex min-h-0 flex-col overflow-hidden border-t"
    >
      <div role="row" className="shrink-0 bg-muted/40">
        <div role="cell" aria-colspan={6} className="font-medium">
          {onToggle ? (
            <button
              type="button"
              aria-label={`${collapsed ? 'Expand' : 'Collapse'} ${label.toLowerCase()}`}
              aria-expanded={!collapsed}
              aria-controls={contentId}
              onClick={onToggle}
              className="flex h-10 w-full items-center gap-3 px-3 text-left hover:bg-muted/60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring"
            >
              {heading}
            </button>
          ) : (
            <div className="flex h-10 items-center gap-3 px-3">{heading}</div>
          )}
        </div>
      </div>
      <div
        id={contentId}
        hidden={collapsed}
        className={collapsed ? 'hidden' : 'flex min-h-0 flex-1 flex-col'}
      >
        <div
          ref={scrollRef}
          role="region"
          tabIndex={0}
          className="min-h-0 flex-1 overflow-y-auto"
          aria-label={`${label} rows`}
        >
          {isLoading || isError || !jobs.length ? (
            <div role="row">
              <div role="cell" aria-colspan={6}>
                {feedback}
              </div>
            </div>
          ) : (
            <div
              className="relative"
              style={{ height: virtualizer.getTotalSize() }}
            >
              {virtualizer.getVirtualItems().map((item) => {
                const job = jobs[item.index];
                return (
                  <div
                    role="row"
                    key={item.key}
                    data-index={item.index}
                    ref={virtualizer.measureElement}
                    className="absolute top-0 left-0 grid min-h-14 w-full border-b bg-background hover:bg-muted/50"
                    style={{
                      gridTemplateColumns: QUEUE_COLUMNS,
                      transform: `translateY(${item.start}px)`,
                    }}
                    {...rowProps(job)}
                  >
                    {renderRow(job)}
                  </div>
                );
              })}
            </div>
          )}
        </div>
        <div role="row" className="shrink-0 border-t bg-background">
          <div role="cell" aria-colspan={6}>
            <QueuePagination
              label={label}
              page={page}
              pageSize={pageSize}
              totalCount={totalCount}
              isFetching={isFetching}
              isError={isError}
              onPageChange={onPageChange}
              onPageSizeChange={onPageSizeChange}
            />
          </div>
        </div>
      </div>
    </div>
  );
}
