import type { Column } from '@tanstack/react-table';
import { ArrowDownIcon, ArrowUpIcon, ChevronsUpDownIcon } from 'lucide-react';
import { useContext } from 'react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { DataTableInteractionContext } from './DataTableInteractionContext';

interface DataTableColumnHeaderProps<TData, TValue> {
  column: Column<TData, TValue>;
  title: string;
  className?: string;
}

export function DataTableColumnHeader<TData, TValue>({
  column,
  title,
  className,
}: DataTableColumnHeaderProps<TData, TValue>) {
  const { sortingPending } = useContext(DataTableInteractionContext);
  if (!column.getCanSort()) {
    return (
      <div className={cn('text-xs font-medium text-foreground', className)}>
        {title}
      </div>
    );
  }

  const sorted = column.getIsSorted();
  const nextOrder = column.getNextSortingOrder();

  return (
    <Button
      type="button"
      disabled={sortingPending}
      aria-description={
        nextOrder === false
          ? 'Restore default order.'
          : `Sort by ${title} in ${nextOrder === 'asc' ? 'ascending' : 'descending'} order.`
      }
      variant="ghost"
      size="sm"
      className={cn(
        '-ml-2 h-7 gap-1 text-xs font-medium text-foreground hover:text-foreground',
        className,
      )}
      onClick={() => {
        const next = column.getNextSortingOrder();
        if (next === false) column.clearSorting();
        else column.toggleSorting(next === 'desc');
      }}
    >
      {title}
      {sorted === 'asc' ? (
        <ArrowUpIcon data-icon="inline-start" />
      ) : sorted === 'desc' ? (
        <ArrowDownIcon data-icon="inline-start" />
      ) : (
        <ChevronsUpDownIcon data-icon="inline-start" className="opacity-50" />
      )}
    </Button>
  );
}
