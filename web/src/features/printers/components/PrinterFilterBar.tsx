import { SearchIcon } from 'lucide-react';
import { Input } from '@/components/ui/input';
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
    <div className="flex flex-wrap items-center gap-3">
      <div className="relative">
        <SearchIcon className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" />
        <Input
          value={query}
          onChange={(event) => onQueryChange(event.target.value)}
          placeholder="Search printers, models, jobs…"
          className="w-64 pl-8"
        />
      </div>
      <ToggleGroup
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
    </div>
  );
}
