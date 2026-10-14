import { DataTableSearch } from '@/components/data-table/DataTableSearch';
import { DataTableToolbar } from '@/components/data-table/DataTableToolbar';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';

export type PrinterStatusFilter =
  | 'all'
  | 'printing'
  | 'idle'
  | 'awaiting'
  | 'maintenance'
  | 'offline';

export interface PrinterFilterCounts {
  all: number;
  printing: number;
  idle: number;
  awaiting: number;
  maintenance: number;
  offline: number;
}

const FILTERS: Array<{ value: PrinterStatusFilter; label: string }> = [
  { value: 'all', label: 'All' },
  { value: 'printing', label: 'Printing' },
  { value: 'idle', label: 'Idle' },
  { value: 'awaiting', label: 'Awaiting' },
  { value: 'maintenance', label: 'Maintenance' },
  { value: 'offline', label: 'Offline' },
];

interface PrinterFilterBarProps {
  query: string;
  onQueryChange: (query: string) => void;
  statusFilter: PrinterStatusFilter;
  onStatusFilterChange: (filter: PrinterStatusFilter) => void;
  counts: PrinterFilterCounts;
}

export function PrinterFilterBar({
  query,
  onQueryChange,
  statusFilter,
  onStatusFilterChange,
  counts,
}: PrinterFilterBarProps) {
  return (
    <DataTableToolbar
      search={
        <DataTableSearch
          value={query}
          onValueChange={onQueryChange}
          label="Search printers by name or model"
          placeholder="Search printers or models…"
        />
      }
    >
      <ToggleGroup
        aria-label="Filter printers by status"
        className="max-w-full flex-wrap justify-start"
        type="single"
        variant="outline"
        value={statusFilter}
        onValueChange={(next) => {
          if (next) onStatusFilterChange(next as PrinterStatusFilter);
        }}
      >
        {FILTERS.map((filter) => (
          <ToggleGroupItem key={filter.value} value={filter.value}>
            {filter.label}
            <span className="ml-1 tabular-nums opacity-70">
              {counts[filter.value]}
            </span>
          </ToggleGroupItem>
        ))}
      </ToggleGroup>
    </DataTableToolbar>
  );
}
