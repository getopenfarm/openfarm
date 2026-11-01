import { Grid2X2Icon, ListIcon } from 'lucide-react';
import type { ReactNode } from 'react';
import type { ServerTableSorting } from '@/components/data-table/DataTable';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { TabsList, TabsTrigger } from '@/components/ui/tabs';
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip';
import type { ViewMode } from '@/lib/preferences/types';

export function LibraryViewControls({
  view,
  sorting,
  columnId,
  disabled,
  fields,
}: {
  view: ViewMode;
  sorting: ServerTableSorting;
  columnId: string;
  disabled?: boolean;
  fields?: ReactNode;
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
      {fields}
      <TooltipProvider>
        <TabsList aria-label="Library view" className="h-8">
          <Tooltip>
            <TabsTrigger value="table" asChild disabled={disabled}>
              <TooltipTrigger aria-label="Details view">
                <ListIcon />
              </TooltipTrigger>
            </TabsTrigger>
            <TooltipContent>Details view</TooltipContent>
          </Tooltip>
          <Tooltip>
            <TabsTrigger value="grid" asChild disabled={disabled}>
              <TooltipTrigger aria-label="Thumbnails view">
                <Grid2X2Icon />
              </TooltipTrigger>
            </TabsTrigger>
            <TooltipContent>Thumbnails view</TooltipContent>
          </Tooltip>
        </TabsList>
      </TooltipProvider>
    </div>
  );
}
