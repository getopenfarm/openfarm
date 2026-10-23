import { useState } from 'react';
import { Swatch } from '@/components/swatch/Swatch';
import { Button } from '@/components/ui/button';
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from '@/components/ui/collapsible';
import { formatWeight } from '@/lib/formatUnits';
import type { ShelfGroup } from '../lib/groupShelf';
import { SpoolsTable } from './SpoolsTable';

/** Identical full rolls on the shelf, one line per color and spool weight with a count; Show lists that group's spools. */
export function ShelfGroups({ groups }: { groups: ShelfGroup[] }) {
  return (
    <ul aria-label="Full rolls" className="divide-y rounded-md border">
      {groups.map((group) => (
        <ShelfGroupRow key={group.key} group={group} />
      ))}
    </ul>
  );
}

function ShelfGroupRow({ group }: { group: ShelfGroup }) {
  const [open, setOpen] = useState(false);
  const name = `${group.colorName} · ${formatWeight(group.spoolWeightGrams)} · full`;
  return (
    <li>
      <Collapsible open={open} onOpenChange={setOpen}>
        <div className="flex items-center gap-3 px-4 py-2.5 text-sm">
          <Swatch colorHex={group.colorHex} size="md" />
          <span className="min-w-0 flex-1 truncate">
            <span className="font-medium">{group.colorName}</span>
            <span className="text-muted-foreground">
              {' '}
              · {formatWeight(group.spoolWeightGrams)} · full
            </span>
          </span>
          <span className="font-medium tabular-nums">
            × {group.spools.length}
          </span>
          <CollapsibleTrigger asChild>
            <Button
              variant="ghost"
              size="sm"
              aria-label={`${open ? 'Hide' : 'Show'} ${name} spools`}
            >
              {open ? 'Hide' : 'Show'}
            </Button>
          </CollapsibleTrigger>
        </div>
        <CollapsibleContent className="border-t p-3">
          <SpoolsTable spools={group.spools} selectable />
        </CollapsibleContent>
      </Collapsible>
    </li>
  );
}
