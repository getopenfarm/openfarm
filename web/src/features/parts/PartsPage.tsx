import { FolderPlusIcon, PlusIcon, SearchIcon } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router';
import { BackLink } from '@/components/back-link/BackLink';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { PageHeader } from '@/components/page-header/PageHeader';
import { PrintQueueDialog } from '@/components/print-queue-dialog/PrintQueueDialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { ViewToggle } from '@/components/view-toggle/ViewToggle';
import { usePreferences } from '@/lib/preferences/usePreferences';
import { FolderTile } from './components/FolderTile';
import { NewFolderDialog } from './components/NewFolderDialog';
import { PartCard } from './components/PartCard';
import { PartsTable } from './components/PartsTable';
import {
  foldersStore,
  type Part,
  useFolders,
  useParts,
} from './data/mockPartsStore';
import { resolveSlotDisplayColor } from './lib/colorMatch';

export function PartsPage() {
  const navigate = useNavigate();
  const [parts] = useParts();
  const [folders] = useFolders();
  const { getListView } = usePreferences();
  const view = getListView('parts');

  const [activeFolderId, setActiveFolderId] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const [newFolderOpen, setNewFolderOpen] = useState(false);
  const [printPart, setPrintPart] = useState<Part | null>(null);

  const activeFolder = folders.find((f) => f.id === activeFolderId) ?? null;
  const normalizedQuery = query.trim().toLowerCase();

  const scopedParts = useMemo(() => {
    return parts.filter((part) => {
      if (part.folderId !== activeFolderId) return false;
      if (!normalizedQuery) return true;
      const haystack = `${part.name} ${part.tags.join(' ')}`.toLowerCase();
      return haystack.includes(normalizedQuery);
    });
  }, [parts, activeFolderId, normalizedQuery]);

  const loosePartsCount = useMemo(
    () => parts.filter((part) => part.folderId === null).length,
    [parts],
  );

  const isSearching = normalizedQuery.length > 0;
  // Folder tiles only show at the library root in grid view. Table view
  // deliberately omits folder rows entirely (the mockup wasn't prescriptive
  // here) — simplest to reason about and keeps DataTable's row type to a
  // single shape (Part), rather than a Part-or-folder union.
  const showFolderTiles = view === 'grid' && !activeFolder;
  const nothingToShow = showFolderTiles
    ? folders.length === 0 && scopedParts.length === 0
    : scopedParts.length === 0;

  function handlePrint(part: Part) {
    setPrintPart(part);
  }

  const printQueuePart = useMemo(() => {
    if (!printPart) return null;
    const primarySlot = printPart.colorSlots[0];
    return {
      name: printPart.name,
      estimatedTimeMinutes: printPart.estimatedTimeMinutes,
      weightGrams: printPart.weightGrams,
      primaryMaterial: primarySlot
        ? {
            colorHex: resolveSlotDisplayColor(primarySlot),
            label: primarySlot.materialType,
          }
        : undefined,
    };
  }, [printPart]);

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        above={
          activeFolder ? (
            <BackLink onClick={() => setActiveFolderId(null)} className="mb-1">
              Part library
            </BackLink>
          ) : undefined
        }
        title={activeFolder ? activeFolder.name : 'Part library'}
        description={
          activeFolder
            ? `${scopedParts.length} ${scopedParts.length === 1 ? 'part' : 'parts'} in this folder`
            : `${folders.length} ${folders.length === 1 ? 'folder' : 'folders'} · ${loosePartsCount} loose parts · ${parts.length} total`
        }
        action={
          <div className="flex items-center gap-2">
            <Button variant="outline" onClick={() => setNewFolderOpen(true)}>
              <FolderPlusIcon /> New folder
            </Button>
            <Button onClick={() => navigate('/parts/new')}>
              <PlusIcon /> Add part
            </Button>
          </div>
        }
      />

      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="relative w-full max-w-sm">
          <SearchIcon className="absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="Search parts…"
            className="pl-8"
          />
        </div>
        <ViewToggle page="parts" />
      </div>

      {nothingToShow ? (
        <EmptyState
          title="Nothing here yet"
          description={
            isSearching
              ? 'Try a different search.'
              : activeFolder
                ? 'Add a part to this folder to get started.'
                : 'Add a part or create a folder to get started.'
          }
        />
      ) : view === 'table' ? (
        <PartsTable
          parts={scopedParts}
          onOpen={(id) => navigate(`/parts/${id}/edit`)}
          onPrint={handlePrint}
        />
      ) : (
        <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 xl:grid-cols-4">
          {showFolderTiles
            ? folders.map((folder) => (
                <FolderTile
                  key={folder.id}
                  name={folder.name}
                  count={
                    parts.filter((part) => part.folderId === folder.id).length
                  }
                  onClick={() => setActiveFolderId(folder.id)}
                />
              ))
            : null}
          {scopedParts.map((part) => (
            <PartCard
              key={part.id}
              part={part}
              onPrint={() => handlePrint(part)}
            />
          ))}
        </div>
      )}

      <NewFolderDialog
        open={newFolderOpen}
        onOpenChange={setNewFolderOpen}
        onCreate={(name) => {
          foldersStore.setState((prev) => [
            ...prev,
            { id: `folder-${crypto.randomUUID()}`, name },
          ]);
        }}
      />

      {printQueuePart ? (
        <PrintQueueDialog
          open={printPart !== null}
          onOpenChange={(open) => {
            if (!open) setPrintPart(null);
          }}
          part={printQueuePart}
        />
      ) : null}
    </div>
  );
}
