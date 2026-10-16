import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { PartDetailPage } from './PartDetailPage';

function renderPage(partId: string) {
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
      <RouterProvider router={router} />
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
    plateCount: 1,
    estimateStatus: 'Unavailable',
    estimatedPrintTimeMinutes: null,
    estimatedFilamentWeightGrams: null,
    filamentRequirements: [],
  };
}

describe('PartDetailPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
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

    expect(await screen.findByText('Removable Part')).toBeInTheDocument();
    const deleteButton = screen.getByRole('button', { name: 'Remove' });
    await user.click(deleteButton);

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

    expect(await screen.findByText('Kept Part')).toBeInTheDocument();
    const deleteButton = screen.getByRole('button', { name: 'Remove' });
    await user.click(deleteButton);

    const dialog = await screen.findByRole('alertdialog');
    expect(within(dialog).getByText('Remove part?')).toBeInTheDocument();
    const cancelButton = within(dialog).getByRole('button', {
      name: 'Cancel',
    });
    await user.click(cancelButton);

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(screen.getByText('Kept Part')).toBeInTheDocument();
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
        expect(await screen.findByText('Recovered part')).toBeInTheDocument();
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
          narrowFilamentProfileId: null,
        },
      ],
    });
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
  });

  it('blocks queueing an incomplete part', async () => {
    const partId = '44444444-4444-4444-4444-444444444444';
    const part = {
      ...partBody(partId, 'Two plates'),
      printRequirementsStatus: 'Incomplete',
      plateCount: 2,
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

    expect(
      await screen.findByRole('button', { name: 'Send to queue' }),
    ).toBeDisabled();
    expect(
      await screen.findByText('Print requirements incomplete'),
    ).toBeVisible();
  });
});
