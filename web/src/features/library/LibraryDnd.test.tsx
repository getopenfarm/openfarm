import { useDragDropManager } from '@dnd-kit/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { RowSelectionState } from '@tanstack/react-table';
import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
} from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useEffect, useRef, useState } from 'react';
import { MemoryRouter } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  BreadcrumbOutlet,
  BreadcrumbOutletProvider,
} from '@/app/layout/BreadcrumbOutlet';
import { Table, TableBody, TableCell } from '@/components/ui/table';
import type { LibraryEntry } from './api';
import { entryRowId } from './api';
import { LibraryTrail } from './LibraryBreadcrumb';
import {
  LibraryDnd,
  LibraryDragHandle,
  LibraryDragRow,
  useLibraryDragSelection,
} from './LibraryDnd';

const entries: LibraryEntry[] = [
  {
    kind: 'part',
    id: 'a',
    name: 'Bracket',
    part: { id: 'a', name: 'Bracket' },
  },
  { kind: 'part', id: 'b', name: 'Clip', part: { id: 'b', name: 'Clip' } },
  { kind: 'part', id: 'c', name: 'Lid', part: { id: 'c', name: 'Lid' } },
  {
    kind: 'folder',
    id: 'f',
    name: 'Fixtures',
    folder: { id: 'f', name: 'Fixtures', parentId: null, library: 'parts' },
  },
  {
    kind: 'folder',
    id: 'g',
    name: 'Products',
    folder: { id: 'g', name: 'Products', parentId: null, library: 'parts' },
  },
];
function setup({
  fail = false,
  parentId = null,
  gate,
  view = 'grid',
}: {
  fail?: boolean;
  parentId?: string | null;
  gate?: Promise<void>;
  view?: 'grid' | 'table';
} = {}) {
  const bridge: { current: ReturnType<typeof useDragDropManager> } = {
    current: null,
  };
  const requests = vi.fn(
    async (_input: RequestInfo | URL, _init?: RequestInit) => {
      await gate;
      return fail
        ? new Response(JSON.stringify({ detail: 'Destination changed.' }), {
            status: 409,
            headers: { 'Content-Type': 'application/json' },
          })
        : new Response(null, { status: 204 });
    },
  );
  vi.stubGlobal('fetch', requests);
  function Contents({
    pendingForBreadcrumb,
  }: {
    pendingForBreadcrumb: boolean;
  }) {
    const manager = useDragDropManager();
    useEffect(() => {
      bridge.current = manager;
    }, [manager]);
    const [selected, setSelected] = useState<RowSelectionState>({
      a: true,
      b: true,
    });
    useLibraryDragSelection(selected, setSelected);
    return (
      <>
        <output aria-label="Selection">
          {Object.keys(selected)
            .filter((id) => selected[id])
            .sort()
            .join(',')}
        </output>
        <LibraryTrail
          sectionLabel={false}
          library="parts"
          ancestors={
            parentId
              ? [
                  {
                    id: 'f',
                    name: 'Fixtures',
                    parentId: null,
                    library: 'parts',
                  },
                  {
                    id: parentId,
                    name: 'Current folder',
                    parentId: 'f',
                    library: 'parts',
                  },
                ]
              : []
          }
          disabled={pendingForBreadcrumb}
          dropTargets
        />
        {view === 'table' ? (
          <Table>
            <TableBody>
              {entries.map((entry) => (
                <LibraryDragRow
                  key={entryRowId(entry)}
                  rowId={entryRowId(entry)}
                  view="table"
                  selected={!!selected[entry.id]}
                >
                  <TableCell>
                    <LibraryDragHandle name={entry.name} />
                    <a href={`/parts/${entry.id}`} draggable={false}>
                      {entry.name}
                    </a>
                  </TableCell>
                </LibraryDragRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          <ul>
            {entries.map((entry) => (
              <LibraryDragRow
                key={entryRowId(entry)}
                rowId={entryRowId(entry)}
                view="grid"
                selected={!!selected[entry.id]}
              >
                <LibraryDragHandle name={entry.name} />
                <img src="/preview.png" alt={entry.name} draggable={false} />
                <a href={`/parts/${entry.id}`} draggable={false}>
                  {entry.name}
                </a>
              </LibraryDragRow>
            ))}
          </ul>
        )}
      </>
    );
  }
  function Harness() {
    const [pending, setPending] = useState(false);
    const [dragging, setDragging] = useState(false);
    const fallback = useRef<HTMLButtonElement>(null);
    return (
      <LibraryDnd
        library="parts"
        entries={entries}
        parentId={parentId}
        disabled={pending}
        onDraggingChange={setDragging}
        onPendingChange={setPending}
        fallbackFocusRef={fallback}
      >
        <button ref={fallback} type="button" disabled={pending}>
          Library
        </button>
        <output aria-label="Pending">{String(pending)}</output>
        <output aria-label="Dragging">{String(dragging)}</output>
        <Contents pendingForBreadcrumb={pending} />
      </LibraryDnd>
    );
  }
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <BreadcrumbOutletProvider>
          <header>
            <BreadcrumbOutlet fallback={null} />
          </header>
          <Harness />
        </BreadcrumbOutletProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  );
  function manager() {
    if (!bridge.current) throw new Error('Manager not mounted');
    return bridge.current;
  }
  async function start(source: string) {
    screen
      .getByRole('button', {
        name: `Move ${entries.find((e) => entryRowId(e) === source)?.name}`,
      })
      .focus();
    await act(async () => {
      manager().actions.start({ source, coordinates: { x: 0, y: 0 } });
    });
    await waitFor(() =>
      expect(screen.getByLabelText('Dragging')).toHaveTextContent('true'),
    );
  }
  async function drop(target: string) {
    await act(async () => {
      await manager().actions.setDropTarget(target);
    });
    await act(async () => {
      manager().actions.stop();
    });
  }
  return { requests, start, drop, manager, user: userEvent.setup() };
}
const restoreMethods: Array<() => void> = [];
function stubMethod(object: object, key: string, value: unknown) {
  const descriptor = Object.getOwnPropertyDescriptor(object, key);
  Object.defineProperty(object, key, { configurable: true, value });
  restoreMethods.push(() => {
    if (descriptor) Object.defineProperty(object, key, descriptor);
    else Reflect.deleteProperty(object, key);
  });
}
beforeEach(() => {
  vi.stubGlobal(
    'IntersectionObserver',
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
      takeRecords() {
        return [];
      }
    },
  );
  // jsdom has no animations or hit testing; pointer tests supply row geometry.
  stubMethod(document, 'elementFromPoint', () => null);
  stubMethod(document, 'getAnimations', () => []);
  stubMethod(Element.prototype, 'getAnimations', () => []);
});
afterEach(() => {
  restoreMethods
    .splice(0)
    .reverse()
    .forEach((restore) => {
      restore();
    });
  vi.unstubAllGlobals();
});
describe('Library drag moves', () => {
  it.each(['table', 'grid'] as const)(
    'moves by dragging the item itself onto the folder in %s view',
    async (view) => {
      stubMethod(
        Element.prototype,
        'getBoundingClientRect',
        function (this: Element) {
          const row = this.closest('[data-library-entry]');
          const index = entries.findIndex(
            (entry) =>
              entryRowId(entry) === row?.getAttribute('data-library-entry'),
          );
          return new DOMRect(
            0,
            index < 0 ? 0 : index * 80,
            600,
            index < 0 ? 700 : 60,
          );
        },
      );
      const { requests } = setup({ view });
      const source =
        view === 'grid'
          ? screen.getByRole('img', { name: 'Lid' })
          : screen.getByRole('link', { name: 'Lid' });
      const target = screen.getByRole('link', { name: 'Fixtures' });
      stubMethod(document, 'elementFromPoint', (_x: number, y: number) =>
        y >= 240 ? target : source,
      );
      function pointer(type: string, element: Element, x: number, y: number) {
        const event = new PointerEvent(type, {
          bubbles: true,
          cancelable: true,
          clientX: x,
          clientY: y,
          button: 0,
          buttons: type === 'pointerup' ? 0 : 1,
        });
        Object.defineProperties(event, {
          isPrimary: { value: true },
          pointerId: { value: 1 },
          pointerType: { value: 'mouse' },
        });
        fireEvent(element, event);
      }
      pointer('pointerdown', source, 30, 180);
      pointer('pointermove', source, 45, 180);
      await waitFor(() =>
        expect(screen.getByLabelText('Dragging')).toHaveTextContent('true'),
      );
      pointer('pointermove', target, 45, 260);
      await waitFor(() =>
        expect(target.closest('[data-library-entry]')).toHaveAttribute(
          'data-drop-target',
          'true',
        ),
      );
      pointer('pointerup', target, 45, 260);
      await waitFor(() => expect(requests).toHaveBeenCalledTimes(1));
      expect(JSON.parse(requests.mock.calls[0][1]?.body as string)).toEqual({
        ids: ['c'],
        folderId: 'f',
      });
    },
  );
  it.each(['table', 'grid'] as const)(
    'moves the selected group atomically in %s view and prunes only after confirmation',
    async (view) => {
      let release: () => void = () => {};
      const gate = new Promise<void>((resolve) => {
        release = resolve;
      });
      const { requests, start, drop } = setup({ gate, view });
      await start('a');
      await drop('drop:folder:f');
      await waitFor(() => expect(requests).toHaveBeenCalledTimes(1));
      expect(JSON.parse(requests.mock.calls[0][1]?.body as string)).toEqual({
        ids: ['a', 'b'],
        folderId: 'f',
      });
      expect(screen.getByLabelText('Selection')).toHaveTextContent('a,b');
      expect(screen.getByLabelText('Pending')).toHaveTextContent('true');
      expect(screen.getByRole('button', { name: 'Move Lid' })).toBeDisabled();
      await act(async () => {
        release();
      });
      await waitFor(() =>
        expect(screen.getByLabelText('Selection')).toBeEmptyDOMElement(),
      );
      expect(screen.getByLabelText('Pending')).toHaveTextContent('false');
    },
  );
  it('moves an unselected item alone and retains the existing selection', async () => {
    const { requests, start, drop } = setup();
    await start('c');
    await drop('drop:folder:f');
    await waitFor(() => expect(requests).toHaveBeenCalledTimes(1));
    expect(JSON.parse(requests.mock.calls[0][1]?.body as string)).toEqual({
      ids: ['c'],
      folderId: 'f',
    });
    expect(screen.getByLabelText('Selection')).toHaveTextContent('a,b');
  });
  it('moves folders individually using the folder endpoint', async () => {
    const { requests, start, drop } = setup();
    await start('folder:f');
    await drop('drop:folder:g');
    await waitFor(() => expect(requests).toHaveBeenCalledTimes(1));
    expect(String(requests.mock.calls[0][0])).toContain('/folders/f/move');
    expect(JSON.parse(requests.mock.calls[0][1]?.body as string)).toEqual({
      parentId: 'g',
    });
    expect(screen.getByLabelText('Selection')).toHaveTextContent('a,b');
  });
  it.each(['root', 'f'])(
    'moves selected items to the local breadcrumb %s',
    async (target) => {
      const { requests, start, drop } = setup({ parentId: 'nested' });
      await start('a');
      expect(
        screen.getByRole('navigation', { name: 'Folder navigation' }),
      ).toContainElement(
        screen.getByRole('link', { name: 'Part library root' }),
      );
      await drop(`breadcrumb:${target}`);
      await waitFor(() => expect(requests).toHaveBeenCalledTimes(1));
      expect(JSON.parse(requests.mock.calls[0][1]?.body as string)).toEqual({
        ids: ['a', 'b'],
        folderId: target === 'root' ? null : target,
      });
    },
  );
  it('retains selection and allows retry when the server rejects the move', async () => {
    const { requests, start, drop } = setup({ fail: true });
    await start('a');
    await drop('drop:folder:f');
    await waitFor(() =>
      expect(screen.getByLabelText('Pending')).toHaveTextContent('false'),
    );
    expect(requests).toHaveBeenCalledTimes(1);
    expect(screen.getByLabelText('Selection')).toHaveTextContent('a,b');
    expect(screen.getByText('Destination changed.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Move Bracket' })).toBeEnabled();
  });
  it('blocks local navigation during a drop and keeps selection available after failure', async () => {
    let release: () => void = () => {};
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const { requests, start, drop } = setup({
      parentId: 'nested',
      fail: true,
      gate,
    });
    await start('a');
    await drop('breadcrumb:root');
    await waitFor(() =>
      expect(screen.getByLabelText('Pending')).toHaveTextContent('true'),
    );
    expect(
      screen.getByRole('link', { name: 'Part library root' }),
    ).toHaveAttribute('aria-disabled', 'true');
    release();
    await waitFor(() =>
      expect(screen.getByLabelText('Pending')).toHaveTextContent('false'),
    );
    expect(screen.getByLabelText('Selection')).toHaveTextContent('a,b');
    expect(
      screen.getByRole('link', { name: 'Part library root' }),
    ).not.toHaveAttribute('aria-disabled');
    await start('a');
    await drop('breadcrumb:f');
    await waitFor(() => expect(requests).toHaveBeenCalledTimes(2));
    expect(screen.getByLabelText('Selection')).toHaveTextContent('a,b');
  });
  it.each(['table', 'grid'] as const)(
    'cancels a pointer drag over its source without revealing a duplicate Move label in %s',
    async (view) => {
      const { requests, manager } = setup({ view });
      const source = document.querySelector<HTMLElement>(
        '[data-library-entry="c"]',
      );
      if (!source) throw new Error('Missing drag source');
      screen.getByRole('button', { name: 'Library' }).focus();
      await act(async () => {
        manager().actions.start({ source: 'c', coordinates: { x: 0, y: 0 } });
      });
      await waitFor(() =>
        expect(screen.getByLabelText('Dragging')).toHaveTextContent('true'),
      );
      expect(screen.queryByText('Move Lid')).not.toBeInTheDocument();
      await act(async () => {
        await manager().actions.setDropTarget('drop:folder:f');
      });
      await act(async () => {
        await manager().actions.setDropTarget(null);
      });
      await act(async () => {
        manager().actions.stop();
      });
      await waitFor(() =>
        expect(screen.getByLabelText('Dragging')).toHaveTextContent('false'),
      );
      expect(requests).not.toHaveBeenCalled();
      expect(screen.getByLabelText('Selection')).toHaveTextContent('a,b');
      expect(source).toHaveFocus();
      expect(screen.queryByText('Move Lid')).not.toBeInTheDocument();
      expect(fireEvent.click(source)).toBe(false);
    },
  );
  it('starts and cancels with the keyboard without submitting a move', async () => {
    const { requests, user } = setup();
    const handle = screen.getByRole('button', { name: 'Move Bracket' });
    handle.focus();
    await user.keyboard(' ');
    await waitFor(() =>
      expect(screen.getByLabelText('Dragging')).toHaveTextContent('true'),
    );
    await user.keyboard('{Escape}');
    await waitFor(() =>
      expect(screen.getByLabelText('Dragging')).toHaveTextContent('false'),
    );
    expect(requests).not.toHaveBeenCalled();
    expect(screen.getByLabelText('Selection')).toHaveTextContent('a,b');
    expect(handle).toHaveFocus();
  });
  it('rejects self and current-location drops without a request', async () => {
    const { requests, start, drop } = setup();
    await start('folder:f');
    await drop('drop:folder:f');
    await waitFor(() =>
      expect(screen.getByLabelText('Dragging')).toHaveTextContent('false'),
    );
    expect(requests).not.toHaveBeenCalled();
    await start('a');
    await drop('breadcrumb:root');
    expect(requests).not.toHaveBeenCalled();
  });
  it('confirms a move through the keyboard sensor', async () => {
    const { requests, user, manager } = setup();
    screen.getByRole('button', { name: 'Move Bracket' }).focus();
    await user.keyboard(' ');
    await waitFor(() =>
      expect(screen.getByLabelText('Dragging')).toHaveTextContent('true'),
    );
    await act(async () => {
      await manager().actions.setDropTarget('drop:folder:f');
    });
    await user.keyboard('{Enter}');
    await waitFor(() => expect(requests).toHaveBeenCalledTimes(1));
    expect(JSON.parse(requests.mock.calls[0][1]?.body as string)).toEqual({
      ids: ['a', 'b'],
      folderId: 'f',
    });
  });
  it('ignores drops onto another item and preserves the displayed sequence', async () => {
    const { requests, start, drop } = setup();
    const before = Array.from(
      document.querySelectorAll('[data-library-entry]'),
    ).map((element) => element.getAttribute('data-library-entry'));
    await start('c');
    await drop('drop:a');
    expect(requests).not.toHaveBeenCalled();
    expect(
      Array.from(document.querySelectorAll('[data-library-entry]')).map(
        (element) => element.getAttribute('data-library-entry'),
      ),
    ).toEqual(before);
    expect(screen.getByLabelText('Selection')).toHaveTextContent('a,b');
  });
  it('recognizes a folder as a drop target before the React drag-state render', () => {
    const { manager } = setup();
    const source = manager().registry.draggables.get('a');
    const folder = manager().registry.droppables.get('drop:folder:f');
    if (!source || !folder)
      throw new Error('Drag entities were not registered');
    expect(folder.accepts(source)).toBe(true);
  });
});
