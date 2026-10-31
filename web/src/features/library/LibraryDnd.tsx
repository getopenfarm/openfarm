import {
  KeyboardSensor,
  PointerActivationConstraints,
  PointerSensor,
} from '@dnd-kit/dom';
import {
  DragDropProvider,
  DragOverlay,
  useDraggable,
  useDroppable,
} from '@dnd-kit/react';
import type { RowSelectionState } from '@tanstack/react-table';
import { FolderInputIcon } from 'lucide-react';
import {
  createContext,
  type Dispatch,
  type MouseEvent,
  type ReactNode,
  type RefObject,
  type SetStateAction,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
} from 'react';
import { toast } from 'sonner';
import type { DataTableRowContainerProps } from '@/components/data-table/DataTable';
import { Button } from '@/components/ui/button';
import { TableRow } from '@/components/ui/table';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import { useAllowed } from '@/lib/auth/AccessContext';
import { cn } from '@/lib/utils';
import {
  entryRowId,
  type LibraryEntry,
  type LibraryKind,
  useLibraryMutation,
} from './api';

interface SelectionAccess {
  ids: string[];
  prune: (ids: string[]) => void;
}
interface DragContextValue {
  entries: LibraryEntry[];
  disabled: boolean;
  parentId: string | null;
  active: { source: LibraryEntry; ids: string[] } | null;
  selection: RefObject<SelectionAccess>;
  suppressClick: (event: MouseEvent<HTMLElement>) => void;
}
const DragContext = createContext<DragContextValue | null>(null);
const HandleContext = createContext<((element: Element | null) => void) | null>(
  null,
);
export function useLibraryDragContext() {
  return useContext(DragContext);
}

export function dragItemIds(source: LibraryEntry, selected: string[]) {
  return source.kind !== 'folder' && selected.includes(source.id)
    ? [...selected]
    : [source.id];
}
export function validLibraryDrop(
  source: LibraryEntry,
  parentId: string | null,
  targetId: string | null,
) {
  return (
    targetId !== parentId &&
    !(source.kind === 'folder' && source.id === targetId)
  );
}

const librarySensors = [
  PointerSensor.configure({
    activatorElements: (source) => [source.element, source.handle],
    activationConstraints: (event) =>
      event.pointerType === 'touch'
        ? [
            new PointerActivationConstraints.Delay({
              value: 250,
              tolerance: 8,
            }),
          ]
        : [new PointerActivationConstraints.Distance({ value: 8 })],
    preventActivation: (event) => {
      const target = event.target instanceof Element ? event.target : null;
      if (target?.closest('[data-library-drag-handle]')) return false;
      return !!target?.closest(
        'button, input, select, textarea, [role="checkbox"], [role="menuitem"]',
      );
    },
  }),
  KeyboardSensor.configure({
    preventActivation: (event) =>
      !(
        event.target instanceof Element &&
        event.target.closest('[data-library-drag-handle]')
      ),
  }),
];

export function LibraryDnd({
  library,
  entries,
  parentId,
  disabled,
  onDraggingChange,
  onPendingChange,
  fallbackFocusRef,
  children,
}: {
  library: LibraryKind;
  entries: LibraryEntry[];
  parentId: string | null;
  disabled: boolean;
  onDraggingChange: (dragging: boolean) => void;
  onPendingChange: (pending: boolean) => void;
  fallbackFocusRef: RefObject<HTMLElement | null>;
  children: ReactNode;
}) {
  const allowed = useAllowed('library:manage');
  const [active, setActive] = useState<DragContextValue['active']>(null);
  const [announcement, setAnnouncement] = useState('');
  const activeRef = useRef(active);
  const selection = useRef<SelectionAccess>({ ids: [], prune: () => {} });
  const suppressUntil = useRef(0);
  const sourceFocus = useRef<HTMLElement | null>(null);
  const mutation = useLibraryMutation(library);
  const suppressClick = useCallback((event: MouseEvent<HTMLElement>) => {
    if (
      (event.target as Element).closest(
        'button, input, select, textarea, [role="checkbox"]',
      )
    )
      return;
    if (Date.now() < suppressUntil.current) {
      event.preventDefault();
      event.stopPropagation();
    }
  }, []);
  return (
    <DragContext.Provider
      value={{
        entries,
        disabled: disabled || !allowed || mutation.isPending,
        parentId,
        active,
        selection,
        suppressClick,
      }}
    >
      <DragDropProvider
        sensors={librarySensors}
        onBeforeDragStart={(event) => {
          if (disabled || !allowed || mutation.isPending)
            event.preventDefault();
        }}
        onDragStart={(event) => {
          const source = entries.find(
            (entry) => entryRowId(entry) === String(event.operation.source?.id),
          );
          if (!source) return;
          const sourceElement = event.operation.source?.element;
          const focused = document.activeElement;
          sourceFocus.current =
            focused instanceof HTMLElement && sourceElement?.contains(focused)
              ? focused
              : sourceElement instanceof HTMLElement
                ? sourceElement
                : null;
          const next = {
            source,
            ids: dragItemIds(source, selection.current.ids),
          };
          activeRef.current = next;
          setActive(next);
          onDraggingChange(true);
          setAnnouncement(
            `Moving ${next.ids.length === 1 ? source.name : `${next.ids.length} items`}. Choose a folder or press Escape to cancel.`,
          );
        }}
        onDragEnd={async (event) => {
          const operation = activeRef.current;
          activeRef.current = null;
          setActive(null);
          onDraggingChange(false);
          if (!operation) return;
          suppressUntil.current = Date.now() + 300;
          const restore = () => {
            const target =
              sourceFocus.current?.isConnected &&
              !sourceFocus.current.matches(':disabled')
                ? sourceFocus.current
                : fallbackFocusRef.current;
            target?.focus();
          };
          const target = event.operation.target;
          if (event.canceled || !target || !('folderId' in target.data)) {
            setAnnouncement('Move canceled.');
            restore();
            return;
          }
          const targetId = target.data.folderId as string | null;
          if (!validLibraryDrop(operation.source, parentId, targetId)) {
            setAnnouncement('Move canceled.');
            restore();
            return;
          }
          onPendingChange(true);
          try {
            await mutation.mutateAsync({
              path:
                operation.source.kind === 'folder'
                  ? `folders/${operation.source.id}/move`
                  : 'items/move',
              method: 'POST',
              body:
                operation.source.kind === 'folder'
                  ? { parentId: targetId }
                  : { ids: operation.ids, folderId: targetId },
            });
            if (operation.source.kind !== 'folder')
              selection.current.prune(operation.ids);
            const message = `${operation.ids.length === 1 ? operation.source.name : `${operation.ids.length} items`} moved to ${String(target.data.name ?? 'folder')}.`;
            setAnnouncement(message);
            toast.success(message);
          } catch (error) {
            const message = await extractErrorDetail(
              error,
              'Could not move items.',
            );
            setAnnouncement(message);
            toast.error(message);
          } finally {
            onPendingChange(false);
            requestAnimationFrame(restore);
          }
        }}
      >
        {children}
        <span role="status" aria-live="polite" className="sr-only">
          {announcement}
        </span>
        <DragOverlay dropAnimation={null}>
          {active && (
            <div className="max-w-64 rounded-md border bg-popover px-3 py-2 text-sm text-popover-foreground shadow-lg">
              {active.ids.length === 1
                ? active.source.name
                : `${active.ids.length} items`}
            </div>
          )}
        </DragOverlay>
      </DragDropProvider>
    </DragContext.Provider>
  );
}

export function useLibraryDragSelection(
  selectionState: RowSelectionState,
  setSelection: Dispatch<SetStateAction<RowSelectionState>>,
) {
  const drag = useContext(DragContext);
  const registry = drag?.selection;
  useEffect(() => {
    if (!registry) return;
    registry.current = {
      ids: Object.keys(selectionState).filter((id) => selectionState[id]),
      prune: (ids) =>
        setSelection((previous) =>
          Object.fromEntries(
            Object.entries(previous).filter(([id]) => !ids.includes(id)),
          ),
        ),
    };
  }, [registry, selectionState, setSelection]);
}
export function LibraryDragHandle({ name }: { name: string }) {
  const ref = useContext(HandleContext);
  const drag = useContext(DragContext);
  return ref ? (
    <Button
      ref={ref}
      data-library-drag-handle
      type="button"
      variant="ghost"
      size="icon-sm"
      className="sr-only focus-visible:not-sr-only"
      aria-label={`Move ${name}`}
      disabled={drag?.disabled}
    >
      <FolderInputIcon />
    </Button>
  ) : null;
}
export function LibraryDragRow({
  rowId,
  view,
  selected,
  children,
  className,
  onClick,
  onClickCapture,
  ...interactionProps
}: DataTableRowContainerProps) {
  const context = useContext(DragContext);
  const contextRef = useRef(context);
  contextRef.current = context;
  const entry = context?.entries.find((entry) => entryRowId(entry) === rowId);
  const entryId = entry?.id;
  const { ref, handleRef, isDragging } = useDraggable({
    id: rowId,
    disabled: !entry || context?.disabled,
    data: { kind: entry?.kind },
  });
  const drop = useDroppable({
    id: `drop:${rowId}`,
    disabled: entry?.kind !== 'folder' || context?.disabled,
    data:
      entry?.kind === 'folder' ? { folderId: entry.id, name: entry.name } : {},
    accept: useCallback(
      (source: { id: string | number }) => {
        const current = contextRef.current;
        const sourceEntry = current?.entries.find(
          (item) => entryRowId(item) === source.id,
        );
        return (
          entryId !== undefined &&
          !!current &&
          !!sourceEntry &&
          validLibraryDrop(sourceEntry, current.parentId, entryId)
        );
      },
      [entryId],
    ),
  });
  const connect = useCallback(
    (element: HTMLElement | null) => {
      ref(element);
      drop.ref(element);
    },
    [ref, drop.ref],
  );
  const props = {
    ...interactionProps,
    ref: connect,
    tabIndex: interactionProps.tabIndex ?? -1,
    'data-state': selected ? 'selected' : undefined,
    'data-drop-target': drop.isDropTarget || undefined,
    'data-library-entry': rowId,
    onClickCapture: (event: MouseEvent<HTMLElement>) => {
      context?.suppressClick(event);
      if (!event.isPropagationStopped()) onClickCapture?.(event);
    },
    onClick,
    className: cn(
      className,
      isDragging && 'opacity-60',
      drop.isDropTarget && 'bg-accent ring-2 ring-inset ring-primary',
    ),
  };
  return (
    <HandleContext.Provider value={handleRef}>
      {view === 'grid' ? (
        <li {...props}>{children}</li>
      ) : (
        <TableRow {...props}>{children}</TableRow>
      )}
    </HandleContext.Provider>
  );
}
export function LibraryBreadcrumbTarget({
  folderId,
  name,
  icon = false,
  children,
}: {
  folderId: string | null;
  name: string;
  icon?: boolean;
  children: ReactNode;
}) {
  const context = useContext(DragContext);
  const contextRef = useRef(context);
  contextRef.current = context;
  const { ref, isDropTarget } = useDroppable({
    id: `breadcrumb:${folderId ?? 'root'}`,
    data: { folderId, name },
    disabled: !context || context.disabled,
    accept: useCallback(
      (source: { id: string | number }) => {
        const current = contextRef.current;
        const sourceEntry = current?.entries.find(
          (item) => entryRowId(item) === source.id,
        );
        return (
          !!current &&
          !!sourceEntry &&
          validLibraryDrop(sourceEntry, current.parentId, folderId)
        );
      },
      [folderId],
    ),
  });
  return (
    <span
      ref={ref}
      onClickCapture={context?.suppressClick}
      className={cn(
        'inline-flex min-w-0',
        icon ? 'rounded-lg' : 'rounded-sm px-2',
        isDropTarget && 'bg-accent ring-2 ring-primary',
        isDropTarget && icon && 'ring-inset',
      )}
    >
      {children}
    </span>
  );
}
