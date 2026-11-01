import { DataTableSearch } from '@/components/data-table/DataTableSearch';
import {
  DataTableTabCount,
  DataTableTabsList,
  DataTableToolbar,
} from '@/components/data-table/DataTableToolbar';
import { getStatusLabel, OFFLINE_META } from '@/components/printer-tile/status';
import { TabsTrigger } from '@/components/ui/tabs';
import { useAllowed } from '@/lib/auth/AccessContext';

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
  { value: 'printing', label: getStatusLabel('Printing') },
  { value: 'idle', label: getStatusLabel('Idle') },
  { value: 'awaiting', label: getStatusLabel('AwaitingClear') },
  { value: 'maintenance', label: 'Maintenance' },
  { value: 'offline', label: OFFLINE_META.label },
];

interface PrinterFilterBarProps {
  query: string;
  onQueryChange: (query: string) => void;
  counts?: PrinterFilterCounts;
}

export function PrinterFilterBar({
  query,
  onQueryChange,
  counts,
}: PrinterFilterBarProps) {
  const showMaintenance = useAllowed('maintenance:view');
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
      <DataTableTabsList aria-label="Filter printers by status">
        {FILTERS.filter(
          (f) => showMaintenance || f.value !== 'maintenance',
        ).map((filter) => (
          <TabsTrigger key={filter.value} value={filter.value}>
            {filter.label}
            <DataTableTabCount count={counts?.[filter.value]} />
          </TabsTrigger>
        ))}
      </DataTableTabsList>
    </DataTableToolbar>
  );
}
