import { ChevronLeftIcon, ChevronRightIcon } from 'lucide-react';
import { Button } from '@/components/ui/button';

interface QueuePaginationProps {
  label: string;
  page: number;
  pageSize: number;
  totalCount: number;
  isFetching: boolean;
  isError: boolean;
  onPageChange: (page: number) => void;
}

export function QueuePagination({
  label,
  page,
  pageSize,
  totalCount,
  isFetching,
  isError,
  onPageChange,
}: QueuePaginationProps) {
  const pageCount = Math.max(1, Math.ceil(totalCount / pageSize));
  return (
    <nav
      aria-label={`${label} pagination`}
      className="flex min-w-0 flex-wrap items-center justify-between gap-x-3 gap-y-1 px-3 py-2"
    >
      <div className="flex min-w-0 items-center gap-2 text-xs">
        <span className="font-medium">{label}</span>
        <span className="text-muted-foreground tabular-nums">
          {isError
            ? 'Unavailable'
            : totalCount
              ? `${(page - 1) * pageSize + 1}–${Math.min(page * pageSize, totalCount)} of ${totalCount}`
              : '0 jobs'}
        </span>
      </div>
      <div className="flex shrink-0 items-center gap-1">
        <Button
          size="icon-sm"
          variant="ghost"
          aria-label={`Previous ${label.toLowerCase()} page`}
          disabled={page <= 1 || isFetching || isError}
          onClick={() => onPageChange(page - 1)}
        >
          <ChevronLeftIcon />
        </Button>
        <span className="text-xs tabular-nums">
          Page {page} of {pageCount}
        </span>
        <Button
          size="icon-sm"
          variant="ghost"
          aria-label={`Next ${label.toLowerCase()} page`}
          disabled={page >= pageCount || isFetching || isError}
          onClick={() => onPageChange(page + 1)}
        >
          <ChevronRightIcon />
        </Button>
      </div>
    </nav>
  );
}
