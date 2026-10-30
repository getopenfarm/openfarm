import { useDragDropManager } from '@dnd-kit/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  BreadcrumbOutlet,
  BreadcrumbOutletProvider,
} from '@/app/layout/BreadcrumbOutlet';
import { Toaster } from '@/components/ui/sonner';
import { AssembliesPage } from '@/features/assemblies/AssembliesPage';
import { PartsPage } from '@/features/parts/PartsPage';
import { PreferencesProvider } from '@/lib/preferences/usePreferences';

const dragBridge = vi.hoisted(() => ({
  current: null as ReturnType<typeof useDragDropManager>,
}));
vi.mock('./LibraryDnd', async (importOriginal) => {
  const original = await importOriginal<typeof import('./LibraryDnd')>();
  function Probe() {
    dragBridge.current = useDragDropManager();
    return null;
  }
  return {
    ...original,
    LibraryDnd: (props: Parameters<typeof original.LibraryDnd>[0]) => (
      <original.LibraryDnd {...props}>
        <Probe />
        {props.children}
      </original.LibraryDnd>
    ),
  };
});

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
    contentsGate?: Promise<void>;
    rejectContents?: boolean;
    initialFolder?: boolean;
    empty?: boolean;
    noFolders?: boolean;
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
      await options.contentsGate;
      if (options.rejectContents) return json({ detail: 'Unavailable.' }, 500);
      const isAssembly = url.pathname.includes('/assemblies/');
      const item = isAssembly
        ? {
            id: 'assembly-one',
            name: 'Enclosure',
            lineCount: 3,
            description: 'Assembly notes',
            previewThumbnails:
              url.searchParams.get('includePreviews') === 'true'
                ? ['AQID']
                : [],
          }
        : {
            id: 'part-one',
            name: 'Bracket',
            fileFormat: 'STL',
            printRequirementsStatus: 'Complete',
            tags: [],
            estimateStatus: 'Available',
            estimatedPrintTimeMinutes: 60,
          };
      const showItem =
        !options.empty &&
        (!options.trackMembership ||
          url.searchParams.get('parentId') === itemFolder);
      return json({
        items: [
          ...(options.noFolders || url.searchParams.has('parentId')
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
          (options.noFolders || url.searchParams.has('parentId') ? 0 : 1) +
          Number(showItem),
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
            fileFormat: 'STL',
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
        <MemoryRouter
          initialEntries={[
            options.initialFolder
              ? `/${options.library ?? 'parts'}?folder=${folder.id}`
              : '/',
          ]}
        >
          <BreadcrumbOutletProvider>
            <header>
              <BreadcrumbOutlet fallback={<span>Default trail</span>} />
            </header>
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
              <Route path="/assemblies" element={<AssembliesPage />} />
              <Route path="/parts/new" element={<Location />} />
              <Route path="/parts/:id" element={<Location />} />
              <Route path="/assemblies/:id" element={<Location />} />
            </Routes>
          </BreadcrumbOutletProvider>
        </MemoryRouter>
        <Toaster />
      </PreferencesProvider>
    </QueryClientProvider>,
  );
  return { calls, user: userEvent.setup() };
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
  localStorage.clear();
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
describe('Library browser', () => {
  it.each(['parts', 'assemblies'] as const)(
    'consolidates %s controls outside the panel and preserves keyboard switching and sorting',
    async (library) => {
      const { user, calls } = setup({ library });
      const name = library === 'parts' ? 'Bracket' : 'Enclosure';
      await screen.findByRole('link', { name });
      const fields = screen.getByRole('button', { name: 'Fields' });
      const toolbar = fields.closest('[data-slot="table-toolbar-row"]');
      expect(toolbar).toContainElement(
        screen.getByRole('tablist', { name: 'Library view' }),
      );
      expect(screen.getByRole('tabpanel')).not.toContainElement(fields);
      expect(
        document.querySelectorAll('[data-slot="table-toolbar-row"]'),
      ).toHaveLength(1);
      const header = screen
        .getByRole('heading', {
          name: library === 'parts' ? 'Part library' : 'Assemblies',
        })
        .closest('[data-slot="page-header"]');
      expect(header).toContainElement(
        screen.getByRole('button', { name: 'Add folder' }),
      );
      expect(header).toContainElement(
        screen.getByRole('button', {
          name: library === 'parts' ? 'Add part' : 'Add assembly',
        }),
      );
      const details = screen.getByRole('tab', { name: 'Details view' });
      act(() => details.focus());
      await user.keyboard('{ArrowRight}');
      const thumbnails = screen.getByRole('tab', { name: 'Thumbnails view' });
      expect(thumbnails).toHaveFocus();
      const sort = screen.getByRole('combobox', { name: 'Sort items' });
      expect(toolbar).toContainElement(sort);
      expect(screen.getByRole('tabpanel')).not.toContainElement(thumbnails);
      await waitFor(() => expect(sort).toBeEnabled());
      await user.click(sort);
      await user.click(screen.getByRole('option', { name: 'Name A–Z' }));
      await waitFor(() =>
        expect(
          calls.mock.calls.some(([input]) => {
            const url = new URL(String(input), 'http://localhost');
            return (
              url.pathname.endsWith('/contents') &&
              url.searchParams.get('sortBy') === 'name' &&
              url.searchParams.get('sortDirection') === 'asc'
            );
          }),
        ).toBe(true),
      );
      await waitFor(() => expect(thumbnails).toBeEnabled());
      act(() => thumbnails.focus());
      await user.keyboard('{ArrowLeft}');
      expect(details).toHaveFocus();
      expect(
        screen.queryByRole('combobox', { name: 'Sort items' }),
      ).not.toBeInTheDocument();
    },
  );

  it.each([
    ['parts', 'table'],
    ['parts', 'grid'],
    ['assemblies', 'table'],
    ['assemblies', 'grid'],
  ] as const)(
    'switches the local %s trail and selection actions in %s view, restoring drop targets during drag',
    async (library, view) => {
      const { user } = setup({ library, initialFolder: true });
      const itemName = library === 'parts' ? 'Bracket' : 'Enclosure';
      const itemId = library === 'parts' ? 'part-one' : 'assembly-one';
      await screen.findByRole('link', { name: itemName });
      if (view === 'grid')
        await user.click(screen.getByRole('tab', { name: 'Thumbnails view' }));
      const trail = screen.getByRole('navigation', {
        name: 'Folder navigation',
      });
      expect(trail.closest('[data-slot="table-toolbar-row"]')).not.toBeNull();
      expect(within(trail).getByText('Brackets')).toHaveAttribute(
        'aria-current',
        'page',
      );
      expect(
        within(trail).getByRole('link', {
          name:
            library === 'parts' ? 'Part library root' : 'Assembly library root',
        }),
      ).toHaveAttribute('href', `/${library}`);
      expect(screen.getByRole('banner')).not.toHaveTextContent('Brackets');
      expect(screen.getByRole('banner')).toHaveTextContent(
        library === 'parts' ? 'LibraryParts' : 'LibraryAssemblies',
      );
      const item = screen.getByRole('link', { name: itemName });
      act(() => item.focus());
      await user.keyboard(' ');
      expect(
        screen.queryByRole('navigation', { name: 'Folder navigation' }),
      ).not.toBeInTheDocument();
      expect(
        screen.getByRole('region', {
          name: library === 'parts' ? 'Parts actions' : 'Assemblies actions',
        }),
      ).toHaveTextContent('1 selected');
      expect(
        screen.getByRole('button', { name: 'Fields' }),
      ).toBeInTheDocument();
      await act(async () => {
        dragBridge.current?.actions.start({
          source: itemId,
          coordinates: { x: 0, y: 0 },
        });
      });
      expect(
        await screen.findByRole('navigation', { name: 'Folder navigation' }),
      ).toBeVisible();
      expect(
        screen.queryByRole('button', { name: 'Clear selection' }),
      ).not.toBeInTheDocument();
      await act(async () => {
        dragBridge.current?.actions.stop();
      });
      expect(
        await screen.findByRole('button', { name: 'Clear selection' }),
      ).toBeVisible();
      expect(
        screen.queryByRole('navigation', { name: 'Folder navigation' }),
      ).not.toBeInTheDocument();
      await user.click(screen.getByRole('button', { name: 'Clear selection' }));
      expect(
        screen.getByRole('navigation', { name: 'Folder navigation' }),
      ).toBeVisible();
    },
  );

  it.each([
    ['parts', 'table'],
    ['parts', 'grid'],
    ['assemblies', 'table'],
    ['assemblies', 'grid'],
  ] as const)(
    'hides the %s trail at root in %s view while retaining Fields and selection actions',
    async (library, view) => {
      const { user } = setup({ library });
      const name = library === 'parts' ? 'Bracket' : 'Enclosure';
      await screen.findByRole('link', { name });
      if (view === 'grid')
        await user.click(screen.getByRole('tab', { name: 'Thumbnails view' }));
      expect(
        screen.queryByRole('navigation', { name: 'Folder navigation' }),
      ).not.toBeInTheDocument();
      expect(
        screen.getByRole('button', { name: 'Fields' }),
      ).toBeInTheDocument();
      act(() => screen.getByRole('link', { name }).focus());
      await user.keyboard(' ');
      expect(
        screen.getByRole('button', { name: 'Clear selection' }),
      ).toBeVisible();
      await user.click(screen.getByRole('button', { name: 'Clear selection' }));
      expect(
        screen.queryByRole('navigation', { name: 'Folder navigation' }),
      ).not.toBeInTheDocument();
      expect(
        screen.getByRole('button', { name: 'Fields' }),
      ).toBeInTheDocument();
    },
  );

  it.each(['parts', 'assemblies'] as const)(
    'hides the %s root trail during loading and errors',
    async (library) => {
      let release = () => {};
      const contentsGate = new Promise<void>((resolve) => {
        release = resolve;
      });
      setup({ library, contentsGate, rejectContents: true });
      expect(screen.getByRole('button', { name: 'Fields' })).toBeDisabled();
      expect(
        screen.getByRole('tab', { name: 'Thumbnails view' }),
      ).toBeDisabled();
      expect(
        screen.queryByRole('navigation', { name: 'Folder navigation' }),
      ).not.toBeInTheDocument();
      release();
      await screen.findByText(`Could not load ${library}`);
      expect(screen.getByRole('button', { name: 'Fields' })).toBeDisabled();
      expect(
        screen.getByRole('tab', { name: 'Thumbnails view' }),
      ).toBeDisabled();
      expect(
        screen.queryByRole('navigation', { name: 'Folder navigation' }),
      ).not.toBeInTheDocument();
    },
  );

  it.each(['parts', 'assemblies'] as const)(
    'hides the %s trail at an empty root',
    async (library) => {
      setup({ library, empty: true, noFolders: true });
      await screen.findByText(
        library === 'parts' ? 'No parts' : 'No assemblies',
      );
      expect(
        screen.queryByRole('navigation', { name: 'Folder navigation' }),
      ).not.toBeInTheDocument();
    },
  );

  it.each(['parts', 'assemblies'] as const)(
    'navigates the local %s ancestor link with the keyboard',
    async (library) => {
      const { user, calls } = setup({ library, initialFolder: true });
      await screen.findByRole('link', {
        name: library === 'parts' ? 'Bracket' : 'Enclosure',
      });
      const trail = screen.getByRole('navigation', {
        name: 'Folder navigation',
      });
      act(() => within(trail).getByRole('link').focus());
      await user.keyboard('{Enter}');
      await waitFor(() =>
        expect(
          calls.mock.calls.some(([input]) => {
            const url = new URL(String(input), 'http://localhost');
            return (
              url.pathname.endsWith('/contents') &&
              !url.searchParams.has('parentId')
            );
          }),
        ).toBe(true),
      );
      expect(
        screen.queryByRole('navigation', { name: 'Folder navigation' }),
      ).not.toBeInTheDocument();
    },
  );

  it.each(['parts', 'assemblies'] as const)(
    'keeps the local %s trail in empty folders',
    async (library) => {
      setup({ library, initialFolder: true, empty: true });
      await screen.findByText('This folder is empty');
      const trail = screen.getByRole('navigation', {
        name: 'Folder navigation',
      });
      expect(trail).toHaveTextContent('Brackets');
      expect(within(trail).getByRole('link')).toHaveAttribute(
        'href',
        `/${library}`,
      );
      expect(screen.getByRole('banner')).not.toHaveTextContent('Brackets');
    },
  );

  it('keeps selection across views, supports keyboard selection and persists the view', async () => {
    const { user } = setup();
    await screen.findByRole('link', { name: 'Bracket' });
    const select = screen.getByRole('checkbox', { name: 'Select Bracket' });
    select.focus();
    await user.keyboard(' ');
    expect(select).toBeChecked();
    await user.click(screen.getByRole('tab', { name: 'Thumbnails view' }));
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(
      screen.getByRole('listitem', { name: 'Bracket, selected' }),
    ).toHaveAttribute('data-state', 'selected');
    expect(
      JSON.parse(localStorage.getItem('openfarm.preferences.v1') ?? '{}')
        .listViews.parts,
    ).toBe('grid');
    await user.click(screen.getByRole('tab', { name: 'Details view' }));
    expect(
      screen.getByRole('checkbox', { name: 'Select Bracket' }),
    ).toBeChecked();
    await user.click(screen.getByRole('button', { name: 'Clear selection' }));
    expect(
      screen.getByRole('checkbox', { name: 'Select all parts on this page' }),
    ).toHaveFocus();
  });
  it.each(['parts', 'assemblies'] as const)(
    'selects %s using modifiers and the card menu while ordinary clicks open',
    async (library) => {
      const { user } = setup({ library });
      const name = library === 'parts' ? 'Bracket' : 'Enclosure';
      const id = library === 'parts' ? 'part-one' : 'assembly-one';
      const link = await screen.findByRole('link', { name });
      const add = screen.getByRole('button', {
        name: library === 'parts' ? 'Add part' : 'Add assembly',
      });
      expect(add.parentElement).toContainElement(
        screen.getByRole('button', { name: 'Add folder' }),
      );
      fireEvent.click(link, { ctrlKey: true });
      expect(
        screen.getByRole('checkbox', { name: `Select ${name}` }),
      ).toBeChecked();
      await user.click(screen.getByRole('tab', { name: 'Thumbnails view' }));
      expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
      const card = screen.getByRole('link', { name }).closest('li');
      expect(card).toHaveAttribute('data-state', 'selected');
      fireEvent.click(screen.getByRole('link', { name }), { metaKey: true });
      expect(card).not.toHaveAttribute('data-state');
      await waitFor(() =>
        expect(
          screen.getByRole('button', { name: `Actions for ${name}` }),
        ).toBeEnabled(),
      );
      await user.click(
        screen.getByRole('button', { name: `Actions for ${name}` }),
      );
      await user.click(screen.getByRole('menuitem', { name: /^Select$/ }));
      expect(card).toHaveAttribute('data-state', 'selected');
      await user.click(screen.getByRole('button', { name: 'Clear selection' }));
      expect(add).toHaveFocus();
      expect(card).not.toHaveAttribute('data-state');
      await user.click(screen.getByRole('link', { name }));
      expect(screen.getByText(`/${library}/${id}`)).toBeInTheDocument();
    },
  );
  it.each(['parts', 'assemblies'] as const)(
    'shares inline actions but saves independent field visibility across %s views',
    async (library) => {
      const { user } = setup({ library });
      const name = library === 'parts' ? 'Bracket' : 'Enclosure';
      const field = library === 'parts' ? 'Estimate' : 'Lines';
      await screen.findByRole('link', { name });
      const fields = screen.getByRole('button', { name: 'Fields' });
      await waitFor(() => expect(fields).toBeEnabled());
      const toolbar = fields.closest('[data-slot="table-toolbar-row"]');
      if (!toolbar) throw new Error('Missing inline toolbar');
      await user.click(
        screen.getByRole('checkbox', { name: `Select ${name}` }),
      );
      const actions = screen.getByRole('region', {
        name: library === 'parts' ? 'Parts actions' : 'Assemblies actions',
      });
      expect(toolbar).toContainElement(actions);
      expect(actions).not.toHaveClass('fixed');
      expect(
        within(toolbar as HTMLElement).getByRole('button', { name: 'Move to' }),
      ).toBeInTheDocument();
      await user.click(fields);
      const toggle = screen.getByRole('menuitemcheckbox', { name: field });
      toggle.focus();
      await user.keyboard(' ');
      expect(toggle).not.toBeChecked();
      await user.keyboard('{Escape}');
      expect(fields).toHaveFocus();
      expect(
        screen.queryByRole('columnheader', { name: field }),
      ).not.toBeInTheDocument();
      const details = screen.getByRole('tab', { name: 'Details view' });
      details.focus();
      await user.keyboard('{ArrowRight}');
      const thumbnails = screen.getByRole('tab', { name: 'Thumbnails view' });
      expect(thumbnails).toHaveFocus();
      expect(thumbnails).toHaveAttribute('aria-selected', 'true');
      const panel = screen.getByRole('tabpanel', { name: 'Thumbnails view' });
      expect(thumbnails).toHaveAttribute('aria-controls', panel.id);
      const card = screen.getByRole('link', { name }).closest('li');
      if (!card) throw new Error('Missing item card');
      expect(card).toHaveAttribute('data-state', 'selected');
      expect(
        within(card).queryByText(field, { selector: 'dt' }),
      ).not.toBeInTheDocument();
      expect(card.querySelector('dl')).not.toBeInTheDocument();
      const gridFields = screen.getByRole('button', { name: 'Fields' });
      expect(
        gridFields.closest('[data-slot="table-toolbar-row"]'),
      ).toContainElement(
        screen.getByRole('region', {
          name: actions.getAttribute('aria-label') ?? '',
        }),
      );
      await waitFor(() => expect(gridFields).toBeEnabled());
      await user.click(gridFields);
      if (library === 'parts') {
        expect(
          screen.queryByRole('menuitemcheckbox', { name: 'Tags' }),
        ).not.toBeInTheDocument();
      }
      expect(
        screen.getByRole('menuitemcheckbox', { name: field }),
      ).not.toBeChecked();
      await user.click(screen.getByRole('menuitemcheckbox', { name: field }));
      await user.keyboard('{Escape}');
      expect(
        within(card).getByText(field, { selector: 'dt' }),
      ).toBeInTheDocument();
      await user.click(screen.getByRole('tab', { name: 'Details view' }));
      const saved = JSON.parse(
        localStorage.getItem('openfarm.preferences.v1') ?? '{}',
      );
      const fieldId = library === 'parts' ? 'estimate' : 'lineCount';
      expect(saved.libraryFields[library].table[fieldId]).toBe(false);
      expect(saved.libraryFields[library].grid[fieldId]).toBe(true);
      expect(
        screen.queryByRole('columnheader', { name: field }),
      ).not.toBeInTheDocument();
      expect(
        screen.getByRole('checkbox', { name: `Select ${name}` }),
      ).toBeChecked();
    },
  );
  it('shows default part preview badges and toggles them without affecting table fields', async () => {
    const { user } = setup();
    await screen.findByRole('link', { name: 'Bracket' });
    await user.click(screen.getByRole('tab', { name: 'Thumbnails view' }));
    const card = screen.getByRole('link', { name: 'Bracket' }).closest('li');
    if (!card) throw new Error('Missing part tile');
    expect(within(card).getByText('STL')).toBeInTheDocument();
    expect(
      within(card).getByRole('button', { name: 'Material: Unknown material' }),
    ).toBeInTheDocument();
    expect(card.querySelector('dl')).not.toBeInTheDocument();
    await user.click(
      within(card).getByRole('button', { name: 'Material: Unknown material' }),
    );
    await user.keyboard('{Enter}');
    expect(screen.getByRole('link', { name: 'Bracket' })).toBeInTheDocument();
    // Missing image uses the preview fallback without removing its metadata.
    expect(card.querySelector('img')).not.toBeInTheDocument();
    const fields = screen.getByRole('button', { name: 'Fields' });
    await user.click(fields);
    await user.click(
      screen.getByRole('menuitemcheckbox', { name: 'File type' }),
    );
    await user.click(
      screen.getByRole('menuitemcheckbox', { name: 'Material' }),
    );
    await user.keyboard('{Escape}');
    expect(fields).toHaveFocus();
    expect(within(card).queryByText('STL')).not.toBeInTheDocument();
    expect(
      within(card).queryByRole('button', { name: /Material:/ }),
    ).not.toBeInTheDocument();
    await user.click(screen.getByRole('tab', { name: 'Details view' }));
    expect(
      screen.getByRole('columnheader', { name: 'File type' }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('columnheader', { name: 'Material' }),
    ).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Fields' }));
    expect(
      screen.getByRole('menuitemcheckbox', { name: 'Tags' }),
    ).toBeInTheDocument();
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
    expect(screen.getByRole('tab', { name: 'Details view' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    await user.click(screen.getByRole('tab', { name: 'Thumbnails view' }));
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
