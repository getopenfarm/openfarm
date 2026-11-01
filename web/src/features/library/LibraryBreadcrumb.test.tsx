import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { Link, MemoryRouter, Route, Routes } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  BreadcrumbOutlet,
  BreadcrumbOutletProvider,
} from '@/app/layout/BreadcrumbOutlet';
import {
  LibraryBreadcrumb,
  LibraryItemBreadcrumb,
  LibraryTrail,
} from './LibraryBreadcrumb';

const ancestors = [
  { id: 'parent', name: 'Hardware', parentId: null, library: 'parts' },
  { id: 'child', name: 'Brackets', parentId: 'parent', library: 'parts' },
];

function setup(content: React.ReactNode) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const user = userEvent.setup();
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <BreadcrumbOutletProvider>
          <header>
            <BreadcrumbOutlet fallback={<span>Other page</span>} />
          </header>
          <main>
            <Routes>
              <Route path="/" element={content} />
              <Route path="/parts" element={<div>Parts destination</div>} />
              <Route
                path="/assemblies"
                element={<div>Assemblies destination</div>}
              />
              <Route path="/other" element={<div>Other content</div>} />
            </Routes>
          </main>
        </BreadcrumbOutletProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  );
  return user;
}

afterEach(() => vi.unstubAllGlobals());

describe('Local library root icon', () => {
  it.each(['parts', 'assemblies'] as const)(
    'labels the current %s root and exposes its tooltip on keyboard focus',
    async (library) => {
      const user = setup(
        <LibraryTrail library={library} ancestors={[]} sectionLabel={false} />,
      );
      const label =
        library === 'parts' ? 'Part library root' : 'Assembly library root';
      const root = screen.getByLabelText(label);
      expect(root).toHaveAttribute('aria-current', 'page');
      expect(root).toHaveAttribute('data-size', 'icon');
      expect(root.querySelector('svg')).toHaveAttribute('aria-hidden', 'true');
      expect(
        screen.getByRole('navigation', { name: 'Folder navigation' }),
      ).not.toHaveTextContent(library === 'parts' ? 'Parts' : 'Assemblies');
      expect(screen.queryByRole('link')).not.toBeInTheDocument();
      await user.tab();
      expect(root).toHaveFocus();
      expect(await screen.findByRole('tooltip')).toHaveTextContent(label);
    },
  );

  it.each(['parts', 'assemblies'] as const)(
    'provides a tooltip and a keyboard root link inside %s folders',
    async (library) => {
      const user = setup(
        <LibraryTrail
          library={library}
          ancestors={ancestors}
          sectionLabel={false}
        />,
      );
      const label =
        library === 'parts' ? 'Part library root' : 'Assembly library root';
      const root = screen.getByRole('link', { name: label });
      expect(root).toHaveAttribute('href', `/${library}`);
      expect(root).not.toHaveAttribute('aria-current');
      await user.hover(root);
      expect(await screen.findByRole('tooltip')).toHaveTextContent(label);
      await user.unhover(root);
      await user.tab();
      expect(root).toHaveFocus();
      await user.keyboard('{Enter}');
      expect(
        screen.getByText(
          `${library === 'parts' ? 'Parts' : 'Assemblies'} destination`,
        ),
      ).toBeInTheDocument();
    },
  );

  it('keeps the icon link disabled while busy', async () => {
    const user = setup(
      <LibraryTrail
        library="parts"
        ancestors={ancestors}
        sectionLabel={false}
        disabled
      />,
    );
    const root = screen.getByRole('link', { name: 'Part library root' });
    expect(root).toHaveAttribute('aria-disabled', 'true');
    await user.click(root);
    expect(screen.queryByText('Parts destination')).not.toBeInTheDocument();
  });
});

describe('Library header breadcrumbs', () => {
  it.each(['parts', 'assemblies'] as const)(
    'renders only one %s trail in the header at root',
    (library) => {
      setup(<LibraryBreadcrumb library={library} ancestors={[]} />);
      expect(
        screen.getAllByRole('navigation', { name: 'breadcrumb' }),
      ).toHaveLength(1);
      const header = within(screen.getByRole('banner'));
      expect(header.getByRole('navigation')).toHaveTextContent(
        `Library${library === 'parts' ? 'Parts' : 'Assemblies'}`,
      );
      expect(
        header.getByText(library === 'parts' ? 'Parts' : 'Assemblies'),
      ).toHaveAttribute('aria-current', 'page');
      expect(
        within(screen.getByRole('main')).queryByRole('navigation'),
      ).not.toBeInTheDocument();
    },
  );

  it.each(['parts', 'assemblies'] as const)(
    'keeps %s ancestor links accessible and clears the trail after navigation',
    async (library) => {
      const user = setup(
        <LibraryBreadcrumb
          library={library}
          ancestors={ancestors}
          currentLabel="Mount"
        />,
      );
      const header = within(screen.getByRole('banner'));
      expect(header.getByRole('link', { name: 'Hardware' })).toHaveAttribute(
        'href',
        `/${library}?folder=parent`,
      );
      expect(header.getByRole('link', { name: 'Brackets' })).toHaveAttribute(
        'href',
        `/${library}?folder=child`,
      );
      expect(header.getByText('Mount')).toHaveAttribute('aria-current', 'page');
      const root = header.getByRole('link', {
        name: library === 'parts' ? 'Parts' : 'Assemblies',
      });
      root.focus();
      await user.keyboard('{Enter}');
      expect(
        screen.getByText(
          `${library === 'parts' ? 'Parts' : 'Assemblies'} destination`,
        ),
      ).toBeInTheDocument();
      expect(header.queryByText('Mount')).not.toBeInTheDocument();
      expect(header.getByText('Other page')).toBeInTheDocument();
    },
  );

  it('marks a folder as the current page and blocks links while a move is pending', async () => {
    const user = setup(
      <LibraryBreadcrumb library="parts" ancestors={ancestors} disabled />,
    );
    expect(screen.getByText('Brackets')).toHaveAttribute(
      'aria-current',
      'page',
    );
    await user.click(screen.getByRole('link', { name: 'Parts' }));
    expect(screen.queryByText('Parts destination')).not.toBeInTheDocument();
  });

  it.each(['parts', 'assemblies'] as const)(
    'resolves saved %s folder membership without URL context and updates the saved name',
    async (library) => {
      const fetch = vi.fn(
        async (_input: RequestInfo | URL) =>
          new Response(
            JSON.stringify({ items: [], totalCount: 0, ancestors }),
            { headers: { 'Content-Type': 'application/json' } },
          ),
      );
      vi.stubGlobal('fetch', fetch);
      function Detail() {
        const [name, setName] = useState('Mount');
        return (
          <>
            <LibraryItemBreadcrumb
              library={library}
              folderId="child"
              name={name}
            />
            <button type="button" onClick={() => setName('Saved mount')}>
              Save name
            </button>
            <Link to="/other">Leave</Link>
          </>
        );
      }
      const user = setup(<Detail />);
      const header = within(screen.getByRole('banner'));
      await header.findByRole('link', { name: 'Brackets' });
      expect(String(fetch.mock.calls[0]?.[0])).toContain(
        `/library/${library}/folders?parentId=child`,
      );
      await user.click(screen.getByRole('button', { name: 'Save name' }));
      expect(header.getByText('Saved mount')).toHaveAttribute(
        'aria-current',
        'page',
      );
      await user.click(screen.getByRole('link', { name: 'Leave' }));
      expect(header.queryByText('Saved mount')).not.toBeInTheDocument();
    },
  );

  it('shows a root item without requesting folder data', () => {
    const fetch = vi.fn();
    vi.stubGlobal('fetch', fetch);
    setup(
      <LibraryItemBreadcrumb
        library="parts"
        folderId={null}
        name="Unfiled part"
      />,
    );
    expect(screen.getByRole('navigation')).toHaveTextContent(
      'LibraryPartsUnfiled part',
    );
    expect(fetch).not.toHaveBeenCalled();
  });

  it('keeps known library context when folder loading fails', async () => {
    const fetch = vi.fn(async () => new Response('{}', { status: 404 }));
    vi.stubGlobal('fetch', fetch);
    setup(
      <LibraryItemBreadcrumb
        library="assemblies"
        folderId="missing"
        name="Assembly"
      />,
    );
    await waitFor(() => expect(fetch).toHaveBeenCalled());
    expect(screen.getByRole('navigation')).toHaveTextContent(
      'LibraryAssembliesAssembly',
    );
    expect(screen.getAllByRole('navigation')).toHaveLength(1);
  });
});
