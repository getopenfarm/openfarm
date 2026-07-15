import { LayoutGridIcon, TableIcon } from 'lucide-react';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import type { ListPageKey } from '@/lib/preferences/types';
import { usePreferences } from '@/lib/preferences/usePreferences';

interface ViewToggleProps {
  page: ListPageKey;
}

export function ViewToggle({ page }: ViewToggleProps) {
  const { getListView, setListView } = usePreferences();
  const view = getListView(page);

  return (
    <ToggleGroup
      type="single"
      variant="outline"
      value={view}
      onValueChange={(next) => {
        if (next === 'table' || next === 'grid') setListView(page, next);
      }}
    >
      <ToggleGroupItem value="table" aria-label="Table view">
        <TableIcon data-icon="inline-start" />
        Table
      </ToggleGroupItem>
      <ToggleGroupItem value="grid" aria-label="Grid view">
        <LayoutGridIcon data-icon="inline-start" />
        Grid
      </ToggleGroupItem>
    </ToggleGroup>
  );
}
