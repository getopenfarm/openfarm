import { Grid2X2Icon, ListIcon } from 'lucide-react';
import type { ServerTableSorting } from '@/components/data-table/DataTable';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import type { ViewMode } from '@/lib/preferences/types';

export function LibraryViewControls({
  view,
  onViewChange,
  sorting,
  columnId,
  disabled,
}: {
  view: ViewMode;
  onViewChange: (view: ViewMode) => void;
  sorting: ServerTableSorting;
  columnId: string;
  disabled?: boolean;
}) {
  const active = sorting.sorting[0];
  return (
    <div className="flex flex-wrap items-center justify-end gap-3">
      {view === 'grid' && (
        <Select
          value={active ? (active.desc ? 'desc' : 'asc') : 'newest'}
          disabled={disabled}
          onValueChange={(v) =>
            sorting.onSortingChange(
              v === 'newest' ? [] : [{ id: columnId, desc: v === 'desc' }],
            )
          }
        >
          <SelectTrigger aria-label="Sort items" className="w-44">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem value="newest">Newest first</SelectItem>
              <SelectItem value="asc">Name A–Z</SelectItem>
              <SelectItem value="desc">Name Z–A</SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
      )}
      <ToggleGroup
        type="single"
        variant="outline"
        value={view}
        disabled={disabled}
        aria-label="Library view"
        onValueChange={(value) => {
          if (value === 'table' || value === 'grid') onViewChange(value);
        }}
      >
        <ToggleGroupItem value="table" aria-label="Details view">
          <ListIcon />
          Details
        </ToggleGroupItem>
        <ToggleGroupItem value="grid" aria-label="Thumbnails view">
          <Grid2X2Icon />
          Thumbnails
        </ToggleGroupItem>
      </ToggleGroup>
    </div>
  );
}
