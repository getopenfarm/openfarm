import { Columns3Icon, Layers3Icon, PlusIcon } from 'lucide-react';
import type { ReactNode } from 'react';
import { useRef, useState } from 'react';
import { useNavigate } from 'react-router';
import {
  useRetainedListData,
  useServerListState,
  useServerPageRecovery,
} from '@/components/data-table/useServerListState';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs, TabsContent } from '@/components/ui/tabs';
import { AssembliesTable } from '@/features/assemblies/components/AssembliesTable';
import { PartsTable } from '@/features/parts/components/PartsTable';
import { usePartsHub } from '@/features/parts/hooks/usePartsHub';
import { pluralize } from '@/lib/pluralize';
import { usePreferences } from '@/lib/preferences/usePreferences';
import {
  type AssemblyLibraryRow,
  type LibraryKind,
  type PartLibraryRow,
  useLibraryContents,
} from './api';
import { LibraryTrail } from './LibraryBreadcrumb';
import { LibraryDnd } from './LibraryDnd';
import { LibraryFolderControls } from './LibraryFolders';
import { LibraryViewControls } from './LibraryViewControls';
import { useLibraryLocation } from './useLibraryLocation';

export function LibraryBrowser({ library }: { library: LibraryKind }) {
  usePartsHub();
  const { folderId } = useLibraryLocation(library);
  return (
    <LibraryBrowserContents
      key={`${library}:${folderId ?? 'root'}`}
      library={library}
    />
  );
}
function LibraryBrowserContents({ library }: { library: LibraryKind }) {
  const { folderId, addLocation, itemLocation } = useLibraryLocation(library);
  const navigate = useNavigate();
  const { getListView, setListView } = usePreferences();
  const view = getListView(library);
  const list = useServerListState('name', 'name');
  const [actionPending, setActionPending] = useState(false);
  const [folderPending, setFolderPending] = useState(false);
  const [dropPending, setDropPending] = useState(false);
  const [dragging, setDragging] = useState(false);
  const addRef = useRef<HTMLButtonElement>(null);
  const query = useLibraryContents(library, {
    ...list.params,
    ...(folderId ? { parentId: folderId } : {}),
    includePreviews: library === 'assemblies' && view === 'grid',
  });
  const data = useRetainedListData(query.data);
  const entries = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const lastPage = Math.max(1, Math.ceil(totalCount / list.pageSize));
  useServerPageRecovery(
    list.page,
    list.pageSize,
    totalCount,
    query.isSuccess && !query.isFetching && !query.isPlaceholderData,
    list.setPage,
  );
  const pending =
    actionPending ||
    folderPending ||
    dropPending ||
    query.isFetching ||
    query.isPlaceholderData;
  const blocked = pending || dragging || query.isError;
  const parts = entries.flatMap((e) => (e.kind === 'part' ? [e.part] : []));
  const assemblies = entries.flatMap((e) =>
    e.kind === 'assembly' ? [e.assembly] : [],
  );
  const partRows: PartLibraryRow[] = entries.flatMap((e) =>
    e.kind === 'folder'
      ? [{ id: e.id, name: e.name, libraryFolder: e.folder }]
      : e.kind === 'part'
        ? [e.part]
        : [],
  );
  const assemblyRows: AssemblyLibraryRow[] = entries.flatMap((e) =>
    e.kind === 'folder'
      ? [{ id: e.id, name: e.name, libraryFolder: e.folder }]
      : e.kind === 'assembly'
        ? [e.assembly]
        : [],
  );
  const pagination = {
    page: list.page,
    pageSize: list.pageSize,
    totalCount,
    onPageChange: list.setPage,
    onPageSizeChange: list.setPageSize,
    disabled: query.isError,
    pending: pending || dragging,
    label: library === 'parts' ? 'Parts' : 'Table',
  };
  const sorting = {
    ...list.serverSorting,
    disabled: query.isError,
    pending: pending || dragging,
  };
  const selectionKey = JSON.stringify([
    folderId,
    list.page,
    list.pageSize,
    list.sorting,
  ]);
  const folderNavigation = folderId ? (
    <LibraryTrail
      library={library}
      ancestors={data?.ancestors ?? []}
      sectionLabel={false}
      disabled={blocked}
      dropTargets
    />
  ) : null;
  const hasTable =
    !!data &&
    entries.length > 0 &&
    !(list.page > lastPage && !query.isPlaceholderData);
  const toolbarControls = (fields: ReactNode) => (
    <LibraryViewControls
      view={view}
      sorting={list.serverSorting}
      columnId="name"
      disabled={blocked}
      fields={fields}
    />
  );
  const entity = library === 'parts' ? 'part' : 'assembly';
  return (
    <LibraryDnd
      library={library}
      entries={entries}
      parentId={folderId}
      disabled={pending || query.isError}
      onDraggingChange={setDragging}
      onPendingChange={setDropPending}
      fallbackFocusRef={addRef}
    >
      <Tabs
        value={view}
        onValueChange={(mode) => {
          if (mode === 'table' || mode === 'grid') setListView(library, mode);
        }}
      >
        <Page>
          <LibraryFolderControls
            header={(addFolder) => (
              <PageHeader
                title={library === 'parts' ? 'Part library' : 'Assemblies'}
                description={
                  data ? pluralize(totalCount, 'entry', 'entries') : undefined
                }
                action={
                  <div className="flex flex-wrap items-center gap-2">
                    {addFolder}
                    <Button
                      ref={addRef}
                      disabled={blocked}
                      onClick={() => navigate(addLocation)}
                    >
                      <PlusIcon data-icon="inline-start" />
                      Add {entity}
                    </Button>
                  </div>
                }
              />
            )}
            library={library}
            folderId={folderId}
            disabled={blocked}
            onPendingChange={setFolderPending}
          >
            {!hasTable && (
              <div
                data-slot="table-toolbar-row"
                className="flex min-h-12 min-w-0 flex-wrap items-center gap-2 rounded-md border px-3 py-2"
              >
                {folderNavigation}
                <div className="ml-auto flex max-w-full flex-wrap items-center justify-end gap-3">
                  {toolbarControls(
                    <Button type="button" variant="outline" size="sm" disabled>
                      <Columns3Icon data-icon="inline-start" /> Fields
                    </Button>,
                  )}
                </div>
              </div>
            )}
            {query.isError && data && (
              <ErrorState
                title="Could not refresh list"
                description="Showing previous results."
                onRetry={() => query.refetch()}
              />
            )}
            {!hasTable && (
              <TabsContent value={view}>
                {query.isPending && !data ? (
                  <Skeleton className="h-64 w-full" />
                ) : query.isError && !data ? (
                  <ErrorState
                    title={`Could not load ${library}`}
                    onRetry={() => query.refetch()}
                  />
                ) : list.page > lastPage && !query.isPlaceholderData ? (
                  <Skeleton className="h-64 w-full" />
                ) : entries.length === 0 ? (
                  <EmptyState
                    icon={
                      library === 'assemblies' ? (
                        <Layers3Icon className="size-8 text-muted-foreground" />
                      ) : undefined
                    }
                    title={
                      folderId
                        ? 'This folder is empty'
                        : library === 'parts'
                          ? 'No parts'
                          : 'No assemblies'
                    }
                    description={`Add a folder or ${entity} here, or use Move to to organize existing items.`}
                    action={
                      <Button
                        disabled={blocked}
                        onClick={() => navigate(addLocation)}
                      >
                        <PlusIcon data-icon="inline-start" />
                        Add {entity}
                      </Button>
                    }
                  />
                ) : null}
              </TabsContent>
            )}
            {hasTable &&
              (library === 'parts' ? (
                <PartsTable
                  parts={parts}
                  folderNavigation={folderNavigation}
                  toolbarControls={toolbarControls}
                  dragging={dragging}
                  libraryRows={partRows}
                  view={view}
                  folderId={folderId}
                  pagination={pagination}
                  sorting={sorting}
                  selectionKey={selectionKey}
                  onPendingChange={setActionPending}
                  fallbackFocusRef={addRef}
                  onRowClick={(row) =>
                    navigate(
                      row.libraryFolder
                        ? `/parts?folder=${row.id}`
                        : itemLocation(row.id ?? ''),
                    )
                  }
                />
              ) : (
                <AssembliesTable
                  assemblies={assemblies}
                  folderNavigation={folderNavigation}
                  toolbarControls={toolbarControls}
                  dragging={dragging}
                  libraryRows={assemblyRows}
                  view={view}
                  folderId={folderId}
                  pagination={pagination}
                  sorting={sorting}
                  selectionKey={selectionKey}
                  onPendingChange={setActionPending}
                  fallbackFocusRef={addRef}
                  onRowClick={(row) =>
                    navigate(
                      row.libraryFolder
                        ? `/assemblies?folder=${row.id}`
                        : itemLocation(row.id ?? ''),
                    )
                  }
                />
              ))}
          </LibraryFolderControls>
        </Page>
      </Tabs>
    </LibraryDnd>
  );
}
