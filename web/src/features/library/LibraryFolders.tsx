import {
  FolderInputIcon,
  PencilIcon,
  PlusIcon,
  Trash2Icon,
} from 'lucide-react';
import {
  createContext,
  type ReactNode,
  useContext,
  useRef,
  useState,
} from 'react';
import { Link } from 'react-router';
import { toast } from 'sonner';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { DataTableRowActions } from '@/components/data-table/DataTableRowActions';
import { PermissionButton as Button } from '@/components/PermissionButton';
import { PermissionMenuItem as DropdownMenuItem } from '@/components/PermissionMenuItem';
import {
  Card,
  CardAction,
  CardContent,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { DropdownMenuGroup } from '@/components/ui/dropdown-menu';
import { Field, FieldGroup, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Spinner } from '@/components/ui/spinner';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import { type Folder, type LibraryKind, useLibraryMutation } from './api';
import { LibraryBreadcrumb } from './LibraryBreadcrumb';
import { LibraryDragHandle } from './LibraryDnd';
import { LibraryPreview } from './LibraryPreview';
import { MoveToDialog } from './MoveToDialog';

export function LibraryFolderControls({
  library,
  folderId,
  children,
  header,
  disabled,
  onPendingChange,
}: {
  library: LibraryKind;
  folderId: string | null;
  children: ReactNode;
  header: (addFolder: ReactNode) => ReactNode;
  disabled?: boolean;
  onPendingChange: (pending: boolean) => void;
}) {
  const mutation = useLibraryMutation(library);
  const [editing, setEditing] = useState<Folder | 'new' | null>(null);
  const [name, setName] = useState('');
  const [removing, setRemoving] = useState<Folder | null>(null);
  const [moving, setMoving] = useState<Folder | null>(null);
  const addRef = useRef<HTMLButtonElement>(null);
  const triggerRef = useRef<HTMLElement | null>(null);
  const pending = mutation.isPending;
  const busy = disabled || pending;
  function startEdit(folder: Folder | 'new', trigger: HTMLElement | null) {
    triggerRef.current = trigger;
    setEditing(folder);
    setName(folder === 'new' ? '' : folder.name);
  }
  async function save() {
    onPendingChange(true);
    try {
      await mutation.mutateAsync({
        path:
          editing === 'new' ? 'folders' : `folders/${(editing as Folder).id}`,
        method: editing === 'new' ? 'POST' : 'PATCH',
        body:
          editing === 'new'
            ? { name: name.trim(), parentId: folderId }
            : { name: name.trim() },
      });
      toast.success(editing === 'new' ? 'Folder added.' : 'Folder renamed.');
      setEditing(null);
    } catch (error) {
      toast.error(await extractErrorDetail(error, 'Could not save folder.'));
    } finally {
      onPendingChange(false);
    }
  }
  async function remove() {
    if (!removing) return;
    onPendingChange(true);
    try {
      await mutation.mutateAsync({
        path: `folders/${removing.id}`,
        method: 'DELETE',
      });
      toast.success('Folder removed.');
      setRemoving(null);
    } catch (error) {
      toast.error(await extractErrorDetail(error, 'Could not remove folder.'));
    } finally {
      onPendingChange(false);
    }
  }
  return (
    <FolderControlsContext.Provider
      value={{
        library,
        busy: !!busy,
        onEdit: (folder, trigger) => startEdit(folder, trigger),
        onMove: (folder, trigger) => {
          triggerRef.current = trigger;
          setMoving(folder);
        },
        onRemove: (folder, trigger) => {
          triggerRef.current = trigger;
          setRemoving(folder);
        },
      }}
    >
      <section aria-label="Library contents" className="flex flex-col gap-4">
        {header(
          <Button
            permission="library:manage"
            ref={addRef}
            variant="outline"
            disabled={busy}
            onClick={() => startEdit('new', addRef.current)}
          >
            <PlusIcon data-icon="inline-start" />
            Add folder
          </Button>,
        )}
        <LibraryBreadcrumb library={library} ancestors={[]} disabled={!!busy} />
        {children}
        <Dialog
          open={editing !== null}
          onOpenChange={(open) => {
            if (!open && !pending) setEditing(null);
          }}
        >
          <DialogContent
            onCloseAutoFocus={(e) => {
              const target = triggerRef.current?.isConnected
                ? triggerRef.current
                : addRef.current;
              if (target) {
                e.preventDefault();
                target.focus();
              }
            }}
          >
            <DialogHeader>
              <DialogTitle>
                {editing === 'new' ? 'Add folder' : 'Rename folder'}
              </DialogTitle>
            </DialogHeader>
            <form
              onSubmit={(e) => {
                e.preventDefault();
                void save();
              }}
              className="flex flex-col gap-4"
            >
              <FieldGroup>
                <Field>
                  <FieldLabel htmlFor={`${library}-folder-name`}>
                    Name
                  </FieldLabel>
                  <Input
                    id={`${library}-folder-name`}
                    autoFocus
                    maxLength={255}
                    value={name}
                    disabled={pending}
                    onChange={(e) => setName(e.target.value)}
                  />
                </Field>
              </FieldGroup>
              <DialogFooter>
                <Button
                  variant="outline"
                  type="button"
                  disabled={pending}
                  onClick={() => setEditing(null)}
                >
                  Cancel
                </Button>
                <Button
                  permission="library:manage"
                  type="submit"
                  disabled={pending || !name.trim()}
                >
                  {pending && <Spinner data-icon="inline-start" />}
                  {pending ? 'Saving…' : 'Save'}
                </Button>
              </DialogFooter>
            </form>
          </DialogContent>
        </Dialog>
        <ConfirmDialog
          open={removing !== null}
          onOpenChange={(open) => {
            if (!open && !pending) setRemoving(null);
          }}
          title="Remove folder?"
          description={`Remove ${removing?.name ?? 'this folder'}? The folder must be empty.`}
          confirmLabel="Remove"
          destructive
          isPending={pending}
          closeOnConfirm={false}
          onConfirm={() => void remove()}
          returnFocusRef={triggerRef}
          fallbackFocusRef={addRef}
        />
        {moving && (
          <MoveToDialog
            library={library}
            folder={moving}
            initialParent={folderId}
            onClose={() => setMoving(null)}
            returnFocusRef={triggerRef}
            fallbackFocusRef={addRef}
            onPendingChange={onPendingChange}
          />
        )}
      </section>
    </FolderControlsContext.Provider>
  );
}
function FolderActions({
  folder,
  disabled,
  onEdit,
  onMove,
  onRemove,
}: {
  folder: Folder;
  disabled?: boolean;
  onEdit: (trigger: HTMLButtonElement | null) => void;
  onMove: (trigger: HTMLButtonElement | null) => void;
  onRemove: (trigger: HTMLButtonElement | null) => void;
}) {
  const ref = useRef<HTMLButtonElement>(null);
  return (
    <DataTableRowActions
      label={folder.name}
      triggerRef={ref}
      disabled={disabled}
    >
      <DropdownMenuGroup>
        <DropdownMenuItem
          permission="library:manage"
          onSelect={() => onEdit(ref.current)}
        >
          <PencilIcon />
          Rename
        </DropdownMenuItem>
        <DropdownMenuItem
          permission="library:manage"
          onSelect={() => onMove(ref.current)}
        >
          <FolderInputIcon />
          Move to
        </DropdownMenuItem>
        <DropdownMenuItem
          permission="library:delete"
          variant="destructive"
          onSelect={() => onRemove(ref.current)}
        >
          <Trash2Icon />
          Remove
        </DropdownMenuItem>
      </DropdownMenuGroup>
    </DataTableRowActions>
  );
}

interface FolderControls {
  library: LibraryKind;
  busy: boolean;
  onEdit: (folder: Folder, trigger: HTMLButtonElement | null) => void;
  onMove: (folder: Folder, trigger: HTMLButtonElement | null) => void;
  onRemove: (folder: Folder, trigger: HTMLButtonElement | null) => void;
}
const FolderControlsContext = createContext<FolderControls | null>(null);
function useFolderControls() {
  const value = useContext(FolderControlsContext);
  if (!value) throw new Error('Folder controls require LibraryFolders.');
  return value;
}
export function LibraryFolderName({
  folder,
  preview = true,
}: {
  folder: Folder;
  preview?: boolean;
}) {
  const { library, busy } = useFolderControls();
  return (
    <div className="flex min-w-0 items-center gap-2">
      <LibraryDragHandle name={folder.name} />
      <Link
        draggable={false}
        aria-disabled={busy || undefined}
        onClick={(e) => {
          if (busy) e.preventDefault();
        }}
        to={`/${library}?folder=${folder.id}`}
        className="flex items-center gap-3"
      >
        {preview && <LibraryPreview kind="folder" compact />}
        <span className="truncate">{folder.name}</span>
      </Link>
    </div>
  );
}
export function LibraryFolderActions({ folder }: { folder: Folder }) {
  const { busy, onEdit, onMove, onRemove } = useFolderControls();
  return (
    <FolderActions
      folder={folder}
      disabled={busy}
      onEdit={(trigger) => onEdit(folder, trigger)}
      onMove={(trigger) => onMove(folder, trigger)}
      onRemove={(trigger) => onRemove(folder, trigger)}
    />
  );
}
export function LibraryFolderCard({ folder }: { folder: Folder }) {
  const { library, busy } = useFolderControls();
  return (
    <Card
      size="sm"
      className="h-full transition-colors group-hover/library-tile:bg-muted/50 group-focus-within/library-tile:bg-muted/50"
    >
      <CardHeader>
        <div className="flex min-w-0 items-center gap-2">
          <CardTitle className="min-w-0">
            <LibraryFolderName folder={folder} preview={false} />
          </CardTitle>
        </div>
        <CardAction>
          <LibraryFolderActions folder={folder} />
        </CardAction>
      </CardHeader>
      <CardContent className="flex min-h-32 flex-1">
        <Link
          draggable={false}
          to={`/${library}?folder=${folder.id}`}
          tabIndex={-1}
          aria-hidden="true"
          onClick={(event) => {
            if (busy) event.preventDefault();
          }}
          className="flex min-h-32 flex-1 items-center justify-center rounded-md bg-muted"
        >
          <LibraryPreview kind="folder" fill />
        </Link>
      </CardContent>
    </Card>
  );
}
