import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { AccessContext } from '@/lib/auth/AccessContext';
import { PartDetailPage } from './PartDetailPage';

function renderPage(partId: string, permissions?: string[]) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  const router = createMemoryRouter(
    [
      { path: '/parts/:id', element: <PartDetailPage /> },
      { path: '/parts', element: <div>Parts list page</div> },
    ],
    { initialEntries: [`/parts/${partId}`] },
  );

  return render(
    <QueryClientProvider client={queryClient}>
      <AccessContext.Provider
        value={permissions ? new Set(permissions) : undefined}
      >
        <RouterProvider router={router} />
      </AccessContext.Provider>
      <Toaster />
    </QueryClientProvider>,
  );
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function partBody(id: string, name: string) {
  return {
    id,
    name,
    fileFormat: 'STL',
    nozzleDiameterMm: 0.4,
    requiresSlicing: true,
    tags: [],
    thumbnails: [],
    plateNumber: 1,
    estimateStatus: 'Unavailable',
    estimatedPrintTimeMinutes: null,
    estimatedFilamentWeightGrams: null,
    filamentRequirements: [],
  };
}

describe('PartDetailPage', () => {
  it('downloads the source without saving or discarding draft edits', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = input.toString();
      if (url.includes('/hubs/farm'))
        return Promise.reject(new Error('No hub'));
      if (url.endsWith('/parts/part-1/file'))
        return Promise.resolve(
          new Response('source bytes', {
            headers: {
              'Content-Disposition': 'attachment; filename="plates.3mf"',
            },
          }),
        );
      if (url.endsWith('/parts/part-1'))
        return Promise.resolve(
          jsonResponse({
            ...partBody('part-1', 'Bracket'),
            originalFileName: 'plates.3mf',
          }),
        );
      return Promise.resolve(jsonResponse({ items: [] }));
    });
    vi.stubGlobal('fetch', fetchMock);
    vi.stubGlobal(
      'URL',
      class extends URL {
        static createObjectURL = vi.fn(() => 'blob:source');
        static revokeObjectURL = vi.fn();
      },
    );
    const click = vi
      .spyOn(HTMLAnchorElement.prototype, 'click')
      .mockImplementation(() => {});
    renderPage('part-1');
    const name = await screen.findByLabelText('Name');
    await user.clear(name);
    await user.type(name, 'Draft name');
    await user.click(screen.getByRole('button', { name: 'File actions' }));
    await user.click(screen.getByRole('menuitem', { name: 'Download file' }));
    await waitFor(() => expect(click).toHaveBeenCalledOnce());
    expect(name).toHaveValue('Draft name');
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled();
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(
      fetchMock.mock.calls.some(([input]) =>
        input.toString().endsWith('/parts/part-1/file'),
      ),
    ).toBe(true);
    expect(
      await screen.findByRole('button', { name: 'File actions' }),
    ).toBeEnabled();
  });

  it('shows the build plate the file was made for in its own row', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = input.toString();
        if (url.includes('/hubs/farm'))
          return Promise.reject(new Error('No hub'));
        if (url.endsWith('/parts/part-1'))
          return Promise.resolve(
            jsonResponse({
              ...partBody('part-1', 'Bracket'),
              estimation: {
                source: 'EmbeddedSlicedMetadata',
                machineModel: 'Bambu Lab P1S',
                buildPlate: 'Textured PEI Plate',
              },
            }),
          );
        return Promise.resolve(jsonResponse({ items: [] }));
      }),
    );
    renderPage('part-1');
    expect(await screen.findByText('Build plate')).toBeInTheDocument();
    expect(screen.getByText('Textured PEI Plate')).toBeInTheDocument();
  });

  it('warns "No plate" when the file names no build plate', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = input.toString();
        if (url.includes('/hubs/farm'))
          return Promise.reject(new Error('No hub'));
        if (url.endsWith('/parts/part-1'))
          return Promise.resolve(
            jsonResponse({
              ...partBody('part-1', 'Bracket'),
              estimation: {
                source: 'EmbeddedSlicedMetadata',
                machineModel: 'Bambu Lab P1S',
              },
            }),
          );
        return Promise.resolve(jsonResponse({ items: [] }));
      }),
    );
    renderPage('part-1');
    expect(await screen.findByText('Build plate')).toBeInTheDocument();
    expect(screen.getByText('No plate')).toBeInTheDocument();
  });

  it('hides Remove when the user cannot remove parts', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(jsonResponse(partBody('part-1', 'Bracket')))),
    );
    renderPage('part-1', ['parts:read']);
    await screen.findByRole('heading', { name: /Bracket/ });
    expect(
      screen.queryByRole('button', { name: 'Remove' }),
    ).not.toBeInTheDocument();
  });

  it('allows retrying a rejected download without losing the part page', async () => {
    let attempts = 0;
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = input.toString();
        if (url.includes('/hubs/farm'))
          return Promise.reject(new Error('No hub'));
        if (url.endsWith('/parts/part-1/file')) {
          attempts++;
          return Promise.resolve(
            attempts === 1
              ? jsonResponse({ detail: 'Source file unavailable.' }, 404)
              : new Response('source'),
          );
        }
        if (url.endsWith('/parts/part-1'))
          return Promise.resolve(jsonResponse(partBody('part-1', 'Bracket')));
        return Promise.resolve(jsonResponse({ items: [] }));
      }),
    );
    vi.stubGlobal(
      'URL',
      class extends URL {
        static createObjectURL = vi.fn(() => 'blob:source');
        static revokeObjectURL = vi.fn();
      },
    );
    const click = vi
      .spyOn(HTMLAnchorElement.prototype, 'click')
      .mockImplementation(() => {});
    const user = userEvent.setup();
    renderPage('part-1');
    await user.click(
      await screen.findByRole('button', { name: 'File actions' }),
    );
    await user.click(screen.getByRole('menuitem', { name: 'Download file' }));
    expect(await screen.findByText('Source file unavailable.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'File actions' })).toBeEnabled();
    await user.click(screen.getByRole('button', { name: 'File actions' }));
    await user.click(screen.getByRole('menuitem', { name: 'Download file' }));
    await waitFor(() => expect(click).toHaveBeenCalledOnce());
    expect(attempts).toBe(2);
  });

  it('keeps dependency-blocked removal safe through the direct Remove button', async () => {
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = input.toString();
      if (url.includes('/hubs/farm'))
        return Promise.reject(new Error('No hub'));
      if (url.endsWith('/deletion-check'))
        return Promise.resolve(
          jsonResponse({
            parts: [
              {
                partId: 'part-1',
                status: 'Blocked',
                blockers: [
                  {
                    code: 'AssemblyReference',
                    entityId: 'assembly-1',
                    entityType: 'Assembly',
                    name: 'Enclosure',
                  },
                ],
                legacyJobsLosingRecovery: 0,
              },
            ],
          }),
        );
      return Promise.resolve(jsonResponse(partBody('part-1', 'Bracket')));
    });
    vi.stubGlobal('fetch', fetchMock);
    const user = userEvent.setup();
    renderPage('part-1');
    const trigger = await screen.findByRole('button', { name: 'Remove' });
    await user.click(trigger);
    expect(await screen.findByText('Cannot remove Bracket')).toBeVisible();
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    await waitFor(() => expect(trigger).toHaveFocus());
    expect(
      fetchMock.mock.calls.filter(([input]) =>
        input.toString().endsWith('/deletion-check'),
      ),
    ).toHaveLength(1);
  });

  it('shows saved folder ancestry when opening a part directly', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = String(input);
        if (url.includes('/library/parts/folders'))
          return Promise.resolve(
            jsonResponse({
              items: [],
              totalCount: 0,
              ancestors: [
                {
                  id: 'hardware',
                  name: 'Hardware',
                  parentId: null,
                  library: 'parts',
                },
              ],
            }),
          );
        if (url.endsWith('/parts/part-1'))
          return Promise.resolve(
            jsonResponse({
              ...partBody('part-1', 'Bracket'),
              folderId: 'hardware',
            }),
          );
        return Promise.resolve(jsonResponse({ items: [], totalCount: 0 }));
      }),
    );
    renderPage('part-1');
    const breadcrumb = within(
      screen.getByRole('navigation', { name: 'breadcrumb' }),
    );
    expect(
      await breadcrumb.findByRole('link', { name: 'Hardware' }),
    ).toHaveAttribute('href', '/parts?folder=hardware');
    expect(breadcrumb.getByText('Bracket')).toHaveAttribute(
      'aria-current',
      'page',
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it.each([
    [true, 'Plain 3MF'],
    [false, 'Sliced 3MF'],
    [undefined, '3MF'],
  ])(
    'formats 3MF with slicing classification %s in the file details',
    async (requiresSlicing, label) => {
      vi.stubGlobal(
        'fetch',
        vi.fn((input: RequestInfo | URL) => {
          if (input.toString().includes('/hubs/farm')) {
            return Promise.reject(new Error('SignalR not available in tests'));
          }
          return Promise.resolve(
            jsonResponse({
              ...partBody('part-1', 'Bracket'),
              fileFormat: 'ThreeMf',
              requiresSlicing,
            }),
          );
        }),
      );
      renderPage('part-1');
      expect(
        await screen.findByText(label, { exact: true }),
      ).toBeInTheDocument();
      expect(screen.queryByText(/ThreeMf/)).not.toBeInTheDocument();
    },
  );

  it('shows a working Remove action that navigates back to the parts list', async () => {
    const partId = '11111111-1111-1111-1111-111111111111';
    const user = userEvent.setup();
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/hubs/farm')) {
        return Promise.reject(new Error('SignalR not available in tests'));
      }
      if (input.toString().endsWith('/deletion-check')) {
        return Promise.resolve(
          new Response(
            JSON.stringify({
              parts: JSON.parse(init?.body as string).partIds.map(
                (partId: string) => ({
                  partId,
                  status: 'Eligible',
                  blockers: [],
                  legacyJobsLosingRecovery: 0,
                }),
              ),
            }),
            { headers: { 'Content-Type': 'application/json' } },
          ),
        );
      }
      if (init?.method === 'DELETE') {
        return Promise.resolve(new Response(null, { status: 204 }));
      }
      return Promise.resolve(jsonResponse(partBody(partId, 'Removable Part')));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderPage(partId);

    expect(
      await screen.findByRole('heading', { name: /Removable Part/ }),
    ).toBeInTheDocument();
    const deleteButton = screen.getByRole('button', { name: 'Remove' });
    deleteButton.focus();
    await user.keyboard('{Enter}');

    const dialog = await screen.findByRole('alertdialog');
    expect(within(dialog).getByText('Remove part?')).toBeInTheDocument();
    const confirmButton = await within(dialog).findByRole('button', {
      name: 'Remove part',
    });
    await user.click(confirmButton);

    expect(await screen.findByText('Parts list page')).toBeInTheDocument();
    expect(
      fetchMock.mock.calls.some(
        (call) =>
          call[0].toString().includes(`/api/v1/parts/${partId}`) &&
          (call[1] as RequestInit | undefined)?.method === 'DELETE',
      ),
    ).toBe(true);
  });

  it('does not delete the part when the confirmation is declined', async () => {
    const partId = '22222222-2222-2222-2222-222222222222';
    const user = userEvent.setup();
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/hubs/farm')) {
        return Promise.reject(new Error('SignalR not available in tests'));
      }
      if (input.toString().endsWith('/deletion-check')) {
        return Promise.resolve(
          new Response(
            JSON.stringify({
              parts: JSON.parse(init?.body as string).partIds.map(
                (partId: string) => ({
                  partId,
                  status: 'Eligible',
                  blockers: [],
                  legacyJobsLosingRecovery: 0,
                }),
              ),
            }),
            { headers: { 'Content-Type': 'application/json' } },
          ),
        );
      }
      if (init?.method === 'DELETE') {
        throw new Error(
          'DELETE should not be called when confirmation is declined',
        );
      }
      return Promise.resolve(jsonResponse(partBody(partId, 'Kept Part')));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderPage(partId);

    expect(
      await screen.findByRole('heading', { name: /Kept Part/ }),
    ).toBeInTheDocument();
    const deleteButton = screen.getByRole('button', { name: 'Remove' });
    deleteButton.focus();
    await user.keyboard('{Enter}');

    const dialog = await screen.findByRole('alertdialog');
    expect(within(dialog).getByText('Remove part?')).toBeInTheDocument();
    const cancelButton = within(dialog).getByRole('button', {
      name: 'Cancel',
    });
    await user.click(cancelButton);

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    await waitFor(() => expect(deleteButton).toHaveFocus());
    expect(
      screen.getByRole('heading', { name: /Kept Part/ }),
    ).toBeInTheDocument();
  });
  it.each([404, 503])(
    'distinguishes a missing part from a load failure (%s)',
    async (status) => {
      let recovered = false;
      vi.stubGlobal(
        'fetch',
        vi.fn((input: RequestInfo | URL) => {
          if (input.toString().includes('/hubs/farm'))
            return Promise.reject(new Error('No hub'));
          return Promise.resolve(
            recovered
              ? jsonResponse(partBody('part-1', 'Recovered part'))
              : jsonResponse({ detail: 'Unavailable' }, status),
          );
        }),
      );
      renderPage('part-1');
      if (status === 404) {
        expect(await screen.findByText('Part not found')).toBeInTheDocument();
        expect(
          screen.queryByRole('button', { name: 'Retry' }),
        ).not.toBeInTheDocument();
      } else {
        expect(
          await screen.findByText('Could not load part'),
        ).toBeInTheDocument();
        recovered = true;
        await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
        expect(
          await screen.findByRole('heading', { name: /Recovered part/ }),
        ).toBeInTheDocument();
      }
    },
  );

  it('stages units and narrowing edits in the draft and saves them in one PUT', async () => {
    const partId = '33333333-3333-3333-3333-333333333333';
    const user = userEvent.setup();
    const part = {
      ...partBody(partId, 'Bracket'),
      tags: ['Brackets'],
      unitsPerPrint: 1,
      filamentRequirements: [
        {
          id: 'req-a',
          materialType: 'PLA',
          detectedColorHex: '#FFFFFF',
          narrowFilamentVariantId: null,
          narrowFilamentProfileId: 'profile-1',
        },
        {
          id: 'req-b',
          materialType: 'PETG',
          detectedColorHex: '#000000',
          narrowFilamentVariantId: null,
          narrowFilamentProfileId: null,
        },
      ],
    };
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      if (url.includes('/hubs/farm'))
        return Promise.reject(new Error('No hub'));
      if (init?.method === 'PUT') {
        const body = JSON.parse(init.body as string);
        return Promise.resolve(
          jsonResponse({
            ...part,
            unitsPerPrint: body.unitsPerPrint,
            filamentRequirements: part.filamentRequirements.map((r) =>
              r.id === 'req-a' ? { ...r, narrowFilamentProfileId: null } : r,
            ),
          }),
        );
      }
      if (url.endsWith(`/api/v1/parts/${partId}`))
        return Promise.resolve(jsonResponse(part));
      return Promise.resolve(jsonResponse({}));
    });
    vi.stubGlobal('fetch', fetchMock);
    const writes = () =>
      fetchMock.mock.calls.filter(([, init]) =>
        ['PUT', 'PATCH'].includes(
          (init as RequestInit | undefined)?.method ?? 'GET',
        ),
      );

    renderPage(partId);

    const units = await screen.findByLabelText('Units per print');
    const save = screen.getByRole('button', { name: 'Save' });
    expect(save).toBeDisabled();

    await user.clear(units);
    await user.type(units, '0');
    await user.click(save);
    expect(
      await screen.findByText('Enter a whole number of at least 1.'),
    ).toBeInTheDocument();

    await user.clear(units);
    await user.type(units, '3');
    await user.click(
      screen.getByRole('button', { name: 'Clear profile constraint' }),
    );
    expect(writes()).toHaveLength(0);

    await user.click(save);

    expect(await screen.findByText('Part saved.')).toBeInTheDocument();
    expect(writes()).toHaveLength(1);
    const [url, init] = writes()[0];
    expect(url.toString()).toContain(`/api/v1/parts/${partId}`);
    expect(JSON.parse((init as RequestInit).body as string)).toEqual({
      name: 'Bracket',
      tags: ['Brackets'],
      unitsPerPrint: 3,
      filamentRequirements: [
        {
          requirementId: 'req-a',
          narrowFilamentVariantId: null,
          narrowFilamentId: null,
          anyColor: false,
          narrowFilamentProfileId: null,
        },
      ],
    });
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
  });

  it.each([false, true])(
    'displays detected units as a stat and explains reprocessing (%s)',
    async (stale) => {
      const part = {
        ...partBody('detected-part', 'Four clips'),
        printRequirementsStatus: 'Complete',
        unitsPerPrint: stale ? 1 : 4,
        detectedUnitsPerPrint: 4,
        quantityDetectionSource: 'GcodeLabels',
        quantityNeedsReprocessing: stale,
      };
      vi.stubGlobal(
        'fetch',
        vi.fn((input: RequestInfo | URL) =>
          input.toString().includes('/hubs/farm')
            ? Promise.reject(new Error('No hub'))
            : Promise.resolve(jsonResponse(part)),
        ),
      );
      renderPage('detected-part');
      const label = await screen.findByText('Units per print');
      expect(label.tagName).toBe('DT');
      expect(label.nextElementSibling).toHaveTextContent(stale ? '1' : '4');
      expect(
        screen.queryByRole('spinbutton', { name: 'Units per print' }),
      ).not.toBeInTheDocument();
      expect(
        screen.queryByRole('button', { name: /Map print units/ }),
      ).not.toBeInTheDocument();
      const queue = screen.getByRole('button', { name: 'Print' });
      if (stale) {
        expect(queue).toBeDisabled();
        expect(screen.getByText(/Detected 4 objects. Reprocess/)).toBeVisible();
      } else {
        expect(queue).toBeEnabled();
        expect(
          screen.queryByText(/Reprocess to update/),
        ).not.toBeInTheDocument();
      }
    },
  );

  it('blocks queueing an incomplete part', async () => {
    const partId = '44444444-4444-4444-4444-444444444444';
    const part = {
      ...partBody(partId, 'Two plates'),
      printRequirementsStatus: 'Incomplete',
      plateNumber: null,
      thumbnails: [
        { plateIndex: 0, thumbnailBase64: 'AAAA' },
        { plateIndex: 1, thumbnailBase64: 'BBBB' },
      ],
    };
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) =>
        input.toString().includes('/hubs/farm')
          ? Promise.reject(new Error('No hub'))
          : Promise.resolve(jsonResponse(part)),
      ),
    );

    renderPage(partId);

    expect(await screen.findByRole('button', { name: 'Print' })).toBeDisabled();
    expect(
      await screen.findByText('Print requirements incomplete'),
    ).toBeVisible();
  });
});
