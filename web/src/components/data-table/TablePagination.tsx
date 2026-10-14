import { ChevronLeftIcon, ChevronRightIcon } from 'lucide-react';
import { useId } from 'react';
import { Button } from '@/components/ui/button';
import { Field, FieldLabel } from '@/components/ui/field';
import {
  Pagination,
  PaginationContent,
  PaginationEllipsis,
  PaginationItem,
} from '@/components/ui/pagination';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';

export interface TablePaginationProps {
  page: number;
  pageSize: number;
  totalCount: number;
  onPageChange: (page: number) => void;
  onPageSizeChange?: (size: number) => void;
  disabled?: boolean;
  label?: string;
  unavailable?: boolean;
}

function pageItems(page: number, count: number): (number | string)[] {
  if (count <= 7) return Array.from({ length: count }, (_, i) => i + 1);
  const visible = new Set([1, count, page - 1, page, page + 1]);
  if (page <= 3)
    [2, 3].forEach((value) => {
      visible.add(value);
    });
  if (page >= count - 2)
    [count - 2, count - 1].forEach((value) => {
      visible.add(value);
    });
  const numbers = [...visible]
    .filter((value) => value >= 1 && value <= count)
    .sort((a, b) => a - b);
  const items: (number | string)[] = [];
  numbers.forEach((value, index) => {
    const previous = numbers[index - 1];
    if (previous && value - previous === 2) items.push(previous + 1);
    else if (previous && value - previous > 2) items.push(`gap-${previous}`);
    items.push(value);
  });
  return items;
}

export function TablePagination({
  page,
  pageSize,
  totalCount,
  onPageChange,
  onPageSizeChange,
  disabled = false,
  label = 'Table',
  unavailable = false,
}: TablePaginationProps) {
  const sizeId = useId();
  const count = Math.max(1, Math.ceil(totalCount / pageSize));
  const current = Math.min(Math.max(1, page), count);
  const start = totalCount ? (current - 1) * pageSize + 1 : 0;
  const end = Math.min(current * pageSize, totalCount);
  return (
    <Pagination
      aria-label={`${label} pagination`}
      className="mx-0 w-full flex-wrap justify-between gap-3 p-3"
    >
      {onPageSizeChange ? (
        <Field orientation="horizontal" className="w-auto">
          <FieldLabel htmlFor={sizeId} className="flex-none">
            Rows per page
          </FieldLabel>
          <Select
            value={String(pageSize)}
            onValueChange={(value) => onPageSizeChange(Number(value))}
            disabled={disabled}
          >
            <SelectTrigger id={sizeId} className="w-20" size="sm">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                {[10, 25, 50, 100].map((size) => (
                  <SelectItem key={size} value={String(size)}>
                    {size}
                  </SelectItem>
                ))}
              </SelectGroup>
            </SelectContent>
          </Select>
        </Field>
      ) : null}
      <div className="ml-auto flex flex-wrap items-center justify-end gap-3">
        <span
          className="text-sm whitespace-nowrap text-muted-foreground tabular-nums"
          aria-live="polite"
        >
          {unavailable ? 'Unavailable' : `${start}–${end} of ${totalCount}`}
        </span>
        <PaginationContent>
          <PaginationItem>
            <Button
              type="button"
              variant="outline"
              size="icon-sm"
              aria-label={`Previous ${label.toLowerCase()} page`}
              disabled={disabled || current <= 1}
              onClick={() => onPageChange(current - 1)}
            >
              <ChevronLeftIcon data-icon="inline-start" />
            </Button>
          </PaginationItem>
          {pageItems(current, count).map((item) => (
            <PaginationItem key={item}>
              {typeof item === 'number' ? (
                <Button
                  type="button"
                  size="icon-sm"
                  variant={item === current ? 'outline' : 'ghost'}
                  aria-label={`Page ${item}`}
                  aria-current={item === current ? 'page' : undefined}
                  disabled={disabled}
                  onClick={() => onPageChange(item)}
                >
                  {item}
                </Button>
              ) : (
                <PaginationEllipsis />
              )}
            </PaginationItem>
          ))}
          <PaginationItem>
            <Button
              type="button"
              variant="outline"
              size="icon-sm"
              aria-label={`Next ${label.toLowerCase()} page`}
              disabled={disabled || current >= count}
              onClick={() => onPageChange(current + 1)}
            >
              <ChevronRightIcon data-icon="inline-end" />
            </Button>
          </PaginationItem>
        </PaginationContent>
      </div>
    </Pagination>
  );
}
