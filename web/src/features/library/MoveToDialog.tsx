import { FolderIcon } from 'lucide-react';
import { type RefObject, useState } from 'react';
import { toast } from 'sonner';
import { DataTable } from '@/components/data-table/DataTable';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Skeleton } from '@/components/ui/skeleton';
import { Spinner } from '@/components/ui/spinner';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import {
  type Folder,
  type LibraryKind,
  useFolders,
  useLibraryMutation,
} from './api';

export function MoveToDialog({
  library,
  ids,
  folder,
  initialParent,
  onClose,
  onMoved,
  returnFocusRef,
  fallbackFocusRef,
  onPendingChange,
}: {
  library: LibraryKind;
  ids?: string[];
  folder?: Folder;
  initialParent: string | null;
  onClose: () => void;
  onMoved?: () => void;
  returnFocusRef?: RefObject<HTMLElement | null>;
  fallbackFocusRef?: RefObject<HTMLElement | null>;
  onPendingChange?: (pending: boolean) => void;
}) {
  const [parentId, setParentId] = useState(initialParent);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const query = useFolders(library, parentId, page, pageSize);
  const mutation = useLibraryMutation(library);
  const data = query.data;
  const excluded = data?.ancestors.some((a) => a.id === folder?.id) ?? false;
  const rows = (data?.items ?? []).filter((f) => f.id !== folder?.id);
  const unchanged = parentId === initialParent;
  const pending = mutation.isPending;
  function go(id: string | null) {
    setParentId(id);
    setPage(1);
  }
  async function move() {
    onPendingChange?.(true);
    try {
      await mutation.mutateAsync({
        path: folder ? `folders/${folder.id}/move` : 'items/move',
        method: 'POST',
        body: folder ? { parentId } : { ids, folderId: parentId },
      });
      toast.success(folder ? 'Folder moved.' : 'Items moved.');
      onMoved?.();
      onClose();
    } catch (error) {
      toast.error(await extractErrorDetail(error, 'Could not move items.'));
    } finally {
      onPendingChange?.(false);
    }
  }
  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open && !pending) onClose();
      }}
    >
      <DialogContent
        onCloseAutoFocus={(event) => {
          const target = returnFocusRef?.current?.isConnected
            ? returnFocusRef.current
            : fallbackFocusRef?.current;
          if (target && !target.matches(':disabled')) {
            event.preventDefault();
            target.focus();
          }
        }}
      >
        <DialogHeader>
          <DialogTitle>Move to</DialogTitle>
        </DialogHeader>
        <nav
          aria-label="Destination path"
          className="flex flex-wrap items-center gap-1"
        >
          <Button
            variant="ghost"
            size="sm"
            disabled={pending}
            onClick={() => go(null)}
          >
            Root
          </Button>
          {data?.ancestors.map((a) => (
            <Button
              key={a.id}
              className="max-w-full whitespace-normal break-all"
              variant="ghost"
              size="sm"
              disabled={pending}
              onClick={() => go(a.id)}
            >
              {a.name}
            </Button>
          ))}
        </nav>
        {query.isError ? (
          <ErrorState
            title="Could not load folders"
            onRetry={() => query.refetch()}
          />
        ) : query.isPending ? (
          <Skeleton className="h-32" />
        ) : rows.length === 0 && (data?.totalCount ?? 0) === 0 ? (
          <EmptyState
            title="No subfolders"
            description="You can move into this location."
          />
        ) : (
          <DataTable
            columns={[
              {
                id: 'name',
                header: 'Folder',
                cell: ({ row }) => (
                  <Button
                    variant="ghost"
                    disabled={pending}
                    onClick={() => go(row.original.id)}
                  >
                    <FolderIcon data-icon="inline-start" />
                    {row.original.name}
                  </Button>
                ),
              },
            ]}
            data={rows}
            getRowId={(f) => f.id}
            serverPagination={{
              page,
              pageSize,
              totalCount: data?.totalCount ?? 0,
              onPageChange: setPage,
              onPageSizeChange: (size) => {
                setPageSize(size);
                setPage(1);
              },
              pending: pending || query.isFetching,
              label: 'Destination folders',
            }}
          />
        )}
        <DialogFooter>
          <Button variant="outline" disabled={pending} onClick={onClose}>
            Cancel
          </Button>
          <Button
            disabled={
              pending ||
              query.isFetching ||
              query.isError ||
              unchanged ||
              excluded
            }
            onClick={() => void move()}
          >
            {pending && <Spinner data-icon="inline-start" />}
            {pending ? 'Moving…' : 'Move here'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
