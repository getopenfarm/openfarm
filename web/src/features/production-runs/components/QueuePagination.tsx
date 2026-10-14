import { TablePagination } from '@/components/data-table/TablePagination';

interface QueuePaginationProps {
  label: string;
  page: number;
  pageSize: number;
  totalCount: number;
  isFetching: boolean;
  isError: boolean;
  onPageChange: (page: number) => void;
  onPageSizeChange: (size: number) => void;
}

export function QueuePagination({
  label,
  page,
  pageSize,
  totalCount,
  isFetching,
  isError,
  onPageChange,
  onPageSizeChange,
}: QueuePaginationProps) {
  return (
    <TablePagination
      label={label}
      page={page}
      pageSize={pageSize}
      totalCount={totalCount}
      disabled={isFetching || isError}
      unavailable={isError}
      onPageChange={onPageChange}
      onPageSizeChange={onPageSizeChange}
    />
  );
}
