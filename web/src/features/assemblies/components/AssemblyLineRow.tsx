import { ChevronDownIcon, ChevronRightIcon } from 'lucide-react';
import { useState } from 'react';
import { Skeleton } from '@/components/ui/skeleton';
import { useOpenFarmApiEndpointsAssembliesGetAssemblyEndpoint } from '@/lib/api/generated/assemblies/assemblies';
import type { BomLineResponse } from '@/lib/api/generated/endpoints.schemas';

interface AssemblyLineRowProps {
  line: BomLineResponse;
  depth: number;
}

/** research.md R7 — a sub-assembly line expands in place by calling GET .../assemblies/{id} again with that line's targetId, lazily, only when expanded. */
export function AssemblyLineRow({ line, depth }: AssemblyLineRowProps) {
  const [expanded, setExpanded] = useState(false);
  const isSubAssembly = line.targetKind === 'Assembly';

  const subAssemblyQuery = useOpenFarmApiEndpointsAssembliesGetAssemblyEndpoint(
    line.targetId ?? '',
    { query: { enabled: isSubAssembly && expanded } },
  );

  return (
    <div>
      <button
        type="button"
        disabled={!isSubAssembly}
        onClick={() => setExpanded((prev) => !prev)}
        className="flex w-full items-center gap-2 rounded-md p-2 text-left hover:bg-muted disabled:cursor-default disabled:hover:bg-transparent"
        style={{ paddingLeft: `${depth * 1.25 + 0.5}rem` }}
      >
        {isSubAssembly ? (
          expanded ? (
            <ChevronDownIcon className="size-4 shrink-0 text-muted-foreground" />
          ) : (
            <ChevronRightIcon className="size-4 shrink-0 text-muted-foreground" />
          )
        ) : (
          <span className="size-4 shrink-0" />
        )}
        <span className="w-40 shrink-0 text-xs text-muted-foreground">
          {line.targetKind}
        </span>
        <span className="flex-1 truncate text-sm font-medium">
          {line.targetName}
        </span>
        <span className="font-mono text-sm">{line.quantity}</span>
      </button>

      {isSubAssembly && expanded && (
        <div>
          {subAssemblyQuery.isLoading ? (
            <div style={{ paddingLeft: `${(depth + 1) * 1.25 + 0.5}rem` }}>
              <Skeleton className="h-6 w-2/3" />
            </div>
          ) : (
            subAssemblyQuery.data?.data?.lines?.map((childLine) => (
              <AssemblyLineRow
                key={childLine.id}
                line={childLine}
                depth={depth + 1}
              />
            ))
          )}
        </div>
      )}
    </div>
  );
}
