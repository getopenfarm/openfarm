import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { AssembliesPage } from '@/features/assemblies/AssembliesPage';
import { PartsPage } from '@/features/parts/PartsPage';
import { PreferencesProvider } from '@/lib/preferences/usePreferences';

vi.mock('@/features/parts/hooks/usePartsHub', () => ({
  usePartsHub: () => {},
}));
const folder = {
  id: 'folder-one',
  name: 'Brackets',
  parentId: null,
  library: 'parts',
};
function json(data: unknown, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}
function Location() {
  return (
    <div>
      {useLocation().pathname}
      {useLocation().search}
    </div>
  );
}
function setup(
  options: {
    trackMembership?: boolean;
    rejectMove?: boolean;
    rejectRemove?: boolean;
    library?: 'parts' | 'assemblies';
  } = {},
) {
  let itemFolder: string | null = null;
  const calls = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://localhost');
    if (url.pathname.endsWith('/items/move')) {
      if (options.rejectMove)
        return json({ detail: 'Destination changed.' }, 409);
      itemFolder = JSON.parse(init?.body as string).folderId;
      return new Response(null, { status: 204 });
    }
    if (url.pathname.endsWith('/contents')) {
      const isAssembly = url.pathname.includes('/assemblies/');
      const item = isAssembly
        ? {
            id: 'assembly-one',
            name: 'Enclosure',
            lineCount: 3,
            previewThumbnails:
              url.searchParams.get('includePreviews') === 'true'
                ? ['AQID']
                : [],
          }
        : {
            id: 'part-one',
            name: 'Bracket',
            fileFormat: 'Stl',
            printRequirementsStatus: 'Complete',
            tags: [],
            estimateStatus: 'Available',
            estimatedPrintTimeMinutes: 60,
          };
      const showItem =
        !options.trackMembership ||
        url.searchParams.get('parentId') === itemFolder;
      return json({
        items: [
          ...(url.searchParams.has('parentId')
            ? []
            : [{ id: folder.id, name: folder.name, kind: 'folder', folder }]),
          ...(showItem
            ? [
                {
                  id: item.id,
                  name: item.name,
                  kind: isAssembly ? 'assembly' : 'part',
                  ...(isAssembly ? { assembly: item } : { part: item }),
                },
              ]
            : []),
        ],
        totalCount:
          (url.searchParams.has('parentId') ? 0 : 1) + Number(showItem),
        ancestors: url.searchParams.has('parentId') ? [folder] : [],
      });
    }
    if (url.pathname.startsWith('/api/v1/library/')) {
      if (init?.method === 'DELETE')
        return json(
          {
            detail:
              'Move all items and subfolders out before removing this folder.',
          },
          409,
        );
      return json({
        items: url.searchParams.has('parentId') ? [] : [folder],
        totalCount: url.searchParams.has('parentId') ? 0 : 1,
        ancestors: url.searchParams.has('parentId') ? [folder] : [],
      });
    }
    if (url.pathname === '/api/v1/assemblies')
      return json({
        items: [
          {
            id: 'assembly-one',
            name: 'Enclosure',
            lineCount: 3,
            previewThumbnails:
              url.searchParams.get('includePreviews') === 'true'
                ? ['AQID']
                : [],
          },
        ],
        totalCount: 1,
      });
    if (url.pathname === '/api/v1/parts')
      return json({
        parts: [
          {
            id: 'part-one',
            name: 'Bracket',
            fileFormat: 'Stl',
            printRequirementsStatus: 'Complete',
            tags: [],
            estimateStatus: 'Available',
            estimatedPrintTimeMinutes: 60,
          },
        ],
        totalCount: 1,
      });
    throw new Error(`Unexpected fetch ${url}`);
  });
  vi.stubGlobal('fetch', calls);
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  render(
    <QueryClientProvider client={client}>
      <PreferencesProvider>
        <MemoryRouter>
          <Routes>
            <Route
              path="/"
              element={
                options.library === 'assemblies' ? (
                  <AssembliesPage />
                ) : (
                  <PartsPage />
                )
              }
            />
            <Route path="/parts" element={<PartsPage />} />
            <Route path="/parts/new" element={<Location />} />
            <Route path="/parts/:id" element={<Location />} />
          </Routes>
        </MemoryRouter>
        <Toaster />
      </PreferencesProvider>
    </QueryClientProvider>,
  );
  return { calls, user: userEvent.setup() };
}
beforeEach(() => localStorage.clear());
afterEach(() => vi.unstubAllGlobals());
describe('Library browser', () => {
  it('keeps selection across views, supports keyboard selection and persists the view', async () => {
    const { user } = setup();
    await screen.findByRole('link', { name: 'Bracket' });
    const select = screen.getByRole('checkbox', { name: 'Select Bracket' });
    select.focus();
    await user.keyboard(' ');
    expect(select).toBeChecked();
    await user.click(screen.getByRole('radio', { name: 'Thumbnails view' }));
    expect(
      screen.getByRole('checkbox', { name: 'Select Bracket' }),
    ).toBeChecked();
    expect(
      JSON.parse(localStorage.getItem('openfarm.preferences.v1') ?? '{}')
        .listViews.parts,
    ).toBe('grid');
    await user.click(screen.getByRole('radio', { name: 'Details view' }));
    expect(
      screen.getByRole('checkbox', { name: 'Select Bracket' }),
    ).toBeChecked();
    await user.click(screen.getByRole('button', { name: 'Clear selection' }));
    expect(
      screen.getByRole('checkbox', { name: 'Select all parts on this page' }),
    ).toHaveFocus();
  });
  it('opens folders using links, resets selection, and carries location into Add part', async () => {
    const { user, calls } = setup();
    await screen.findByRole('link', { name: 'Brackets' });
    await user.click(screen.getByRole('checkbox', { name: 'Select Bracket' }));
    await user.click(screen.getByRole('link', { name: 'Brackets' }));
    await waitFor(() =>
      expect(
        calls.mock.calls.some(([url]) =>
          String(url).includes('parentId=folder-one'),
        ),
      ).toBe(true),
    );
    expect(
      screen.queryByRole('region', { name: 'Parts actions' }),
    ).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Add part' }));
    expect(
      screen.getByText('/parts/new?folder=folder-one'),
    ).toBeInTheDocument();
  });
  it('refreshes the source and destination after dropping a part onto a folder row', async () => {
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
    vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(
      function (this: Element) {
        const row = this.closest('[data-library-entry]');
        return new DOMRect(
          0,
          row?.getAttribute('data-library-entry') === 'part-one' ? 100 : 0,
          600,
          row ? 60 : 700,
        );
      },
    );
    const animations = () => [];
    Object.defineProperty(document, 'getAnimations', {
      configurable: true,
      value: animations,
    });
    Object.defineProperty(Element.prototype, 'getAnimations', {
      configurable: true,
      value: animations,
    });
    const { calls, user } = setup({ trackMembership: true });
    try {
      const source = await screen.findByRole('link', { name: 'Bracket' });
      const target = screen.getByRole('link', { name: 'Brackets' });
      Object.defineProperty(document, 'elementFromPoint', {
        configurable: true,
        value: (_x: number, y: number) => (y < 60 ? target : source),
      });
      function pointer(type: string, element: Element, x: number, y: number) {
        fireEvent(
          element,
          new PointerEvent(type, {
            bubbles: true,
            cancelable: true,
            isPrimary: true,
            pointerId: 1,
            pointerType: 'mouse',
            button: 0,
            buttons: type === 'pointerup' ? 0 : 1,
            clientX: x,
            clientY: y,
          }),
        );
      }
      pointer('pointerdown', source, 30, 120);
      pointer('pointermove', source, 45, 120);
      await screen.findByText(
        'Moving Bracket. Choose a folder or press Escape to cancel.',
      );
      pointer('pointermove', target, 45, 20);
      await waitFor(() =>
        expect(target.closest('[data-library-entry]')).toHaveAttribute(
          'data-drop-target',
          'true',
        ),
      );
      pointer('pointerup', target, 45, 20);
      await waitFor(() =>
        expect(
          screen.queryByRole('link', { name: 'Bracket' }),
        ).not.toBeInTheDocument(),
      );
      expect(
        calls.mock.calls.some(
          ([url, init]) =>
            String(url).endsWith('/items/move') &&
            init?.body ===
              JSON.stringify({ ids: ['part-one'], folderId: 'folder-one' }),
        ),
      ).toBe(true);
      // Wait out the post-drop click suppression before opening the destination.
      await new Promise((resolve) => setTimeout(resolve, 310));
      await user.click(screen.getByRole('link', { name: 'Brackets' }));
      expect(
        await screen.findByRole('link', { name: 'Bracket' }),
      ).toBeInTheDocument();
    } finally {
      vi.restoreAllMocks();
      Reflect.deleteProperty(document, 'elementFromPoint');
      Reflect.deleteProperty(document, 'getAnimations');
      Reflect.deleteProperty(Element.prototype, 'getAnimations');
    }
  });
  it('retains selection and the move dialog when a move is refused', async () => {
    const { user } = setup({ rejectMove: true });
    await screen.findByRole('checkbox', { name: 'Select Bracket' });
    await user.click(screen.getByRole('checkbox', { name: 'Select Bracket' }));
    await user.click(
      within(screen.getByRole('region', { name: 'Parts actions' })).getByRole(
        'button',
        { name: 'Move to' },
      ),
    );
    const dialog = screen.getByRole('dialog', { name: 'Move to' });
    await user.click(
      await within(dialog).findByRole('button', { name: 'Brackets' }),
    );
    await user.click(within(dialog).getByRole('button', { name: 'Move here' }));
    await screen.findByText('Destination changed.');
    expect(dialog).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    expect(
      screen.getByRole('checkbox', { name: 'Select Bracket' }),
    ).toBeChecked();
    expect(
      within(screen.getByRole('region', { name: 'Parts actions' })).getByRole(
        'button',
        { name: 'Move to' },
      ),
    ).toHaveFocus();
  });
  it('keeps the destructive confirmation open when a folder is not empty', async () => {
    const { user } = setup();
    const actions = await screen.findByRole('button', {
      name: 'Actions for Brackets',
    });
    await waitFor(() => expect(actions).toBeEnabled());
    await user.click(actions);
    await user.click(screen.getByRole('menuitem', { name: 'Remove' }));
    const dialog = screen.getByRole('alertdialog', { name: 'Remove folder?' });
    await user.click(within(dialog).getByRole('button', { name: 'Remove' }));
    await screen.findByText(
      'Move all items and subfolders out before removing this folder.',
    );
    expect(dialog).toBeInTheDocument();
  });
  it('loads assembly collages on demand and remembers its view independently', async () => {
    localStorage.setItem(
      'openfarm.preferences.v1',
      JSON.stringify({ listViews: { parts: 'grid' } }),
    );
    const { user, calls } = setup({ library: 'assemblies' });
    await screen.findByRole('link', { name: 'Enclosure' });
    expect(screen.getByRole('radio', { name: 'Details view' })).toBeChecked();
    await user.click(screen.getByRole('radio', { name: 'Thumbnails view' }));
    await waitFor(() =>
      expect(
        calls.mock.calls.some(([url]) =>
          String(url).includes('includePreviews=true'),
        ),
      ).toBe(true),
    );
    const card = screen
      .getByRole('link', { name: 'Enclosure' })
      .closest('[data-slot="card"]');
    await waitFor(() =>
      expect(card?.querySelector('img')).toHaveAttribute(
        'src',
        'data:image/png;base64,AQID',
      ),
    );
    expect(
      JSON.parse(localStorage.getItem('openfarm.preferences.v1') ?? '{}')
        .listViews,
    ).toEqual({ parts: 'grid', assemblies: 'grid' });
  });
  it('carries the containing folder into item detail links', async () => {
    const { user } = setup();
    await user.click(await screen.findByRole('link', { name: 'Brackets' }));
    await user.click(await screen.findByRole('link', { name: 'Bracket' }));
    expect(
      screen.getByText('/parts/part-one?folder=folder-one'),
    ).toBeInTheDocument();
  });
  it('renders one mixed collection and selects only items', async () => {
    const { user } = setup();
    await screen.findByRole('link', { name: 'Bracket' });
    expect(
      within(
        screen.getByRole('region', { name: 'Library contents' }),
      ).getAllByRole('table'),
    ).toHaveLength(1);
    expect(
      screen.getByRole('columnheader', { name: 'Name' }),
    ).toBeInTheDocument();
    expect(screen.getAllByRole('checkbox')).toHaveLength(2);
    expect(
      screen.getByRole('navigation', { name: 'Parts pagination' }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('navigation', { name: 'Folders pagination' }),
    ).not.toBeInTheDocument();
    await user.click(
      screen.getByRole('checkbox', { name: 'Select all parts on this page' }),
    );
    expect(
      screen.getByRole('checkbox', { name: 'Select Bracket' }),
    ).toBeChecked();
    expect(
      screen.getByRole('region', { name: 'Parts actions' }),
    ).toHaveTextContent('1 selected');
  });
});
