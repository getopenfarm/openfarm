import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { toast } from 'sonner';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import type { PartSummaryResponse } from '@/lib/api/generated/endpoints.schemas';
import { PartsTable } from './PartsTable';

const parts: PartSummaryResponse[] = [
  { id: 'a', name: 'Bracket', printRequirementsStatus: 'Complete' },
  { id: 'b', name: 'Cover', printRequirementsStatus: 'Incomplete' },
];
function noticeToast() {
  return document.querySelector(
    '[data-sonner-toast][data-removed="false"]:is([data-type="warning"], [data-type="error"], [data-type="info"])',
  );
}

function setup(fetchMock = vi.fn(), checkMock?: (ids: string[]) => Response) {
  vi.stubGlobal('fetch', (input: RequestInfo | URL, init?: RequestInit) => {
    if (input.toString().endsWith('/deletion-check')) {
      if (checkMock)
        return Promise.resolve(
          checkMock(JSON.parse(init?.body as string).partIds),
        );
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
    return fetchMock(input, init);
  });
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  client.setQueryData(['/api/v1/parts/a'], { data: parts[0] });
  client.setQueryData(['/api/v1/parts/b'], { data: parts[1] });
  const onRowClick = vi.fn();
  const onPendingChange = vi.fn();
  const view = (data: PartSummaryResponse[], selectionKey: string) => (
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <PartsTable
          parts={data}
          selectionKey={selectionKey}
          onRowClick={onRowClick}
          onPendingChange={onPendingChange}
        />
        <Toaster />
      </QueryClientProvider>
    </MemoryRouter>
  );
  const result = render(view(parts, 'page-1'));
  return {
    client,
    onRowClick,
    onPendingChange,
    rerender: (data: PartSummaryResponse[], key = 'page-1') =>
      result.rerender(view(data, key)),
  };
}
async function selectAllAndOpen() {
  await userEvent.click(
    screen.getByRole('checkbox', { name: 'Select all parts on this page' }),
  );
  await userEvent.click(
    screen.getByRole('button', { name: 'Remove selected' }),
  );
}
function failure() {
  return new Response(
    JSON.stringify({
      code: 'PartDeletionBlocked',
      detail: 'Referenced by assembly Enclosure.',
    }),
    { status: 409, headers: { 'Content-Type': 'application/problem+json' } },
  );
}
function success() {
  return new Response(null, { status: 204 });
}

afterEach(() => {
  toast.dismiss();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('Part Library selection and bulk removal', () => {
  it('shows blocked-only feedback in a toast without opening a modal or changing selection', async () => {
    const deletes = vi.fn();
    setup(
      deletes,
      (ids) =>
        new Response(
          JSON.stringify({
            parts: ids.map((partId) => ({
              partId,
              status: 'Blocked',
              legacyJobsLosingRecovery: 0,
              blockers: [
                {
                  code: 'AssemblyReference',
                  entityId: 'assembly-1',
                  entityType: 'Assembly',
                  name: 'Slab Ass',
                },
              ],
            })),
          }),
          { headers: { 'Content-Type': 'application/json' } },
        ),
    );
    await selectAllAndOpen();
    const bracketToast = (
      await screen.findByText('Cannot remove Bracket')
    ).closest('[data-sonner-toast]');
    const coverToast = (await screen.findByText('Cannot remove Cover')).closest(
      '[data-sonner-toast]',
    );
    expect(bracketToast).toHaveAttribute('data-type', 'warning');
    expect(coverToast).toHaveAttribute('data-type', 'warning');
    expect(bracketToast).toHaveTextContent('Used by assembly: Slab Ass');
    expect(bracketToast).not.toHaveTextContent('Cover');
    expect(coverToast).not.toHaveTextContent('Bracket');
    expect(
      document.querySelectorAll(
        '[data-sonner-toast][data-type="warning"][data-removed="false"]',
      ),
    ).toHaveLength(2);
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Cancel' }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole('checkbox', { name: 'Select Cover' }),
    ).toBeChecked();
    expect(
      screen.getByRole('checkbox', { name: 'Select Bracket' }),
    ).toBeChecked();
    expect(deletes).not.toHaveBeenCalled();
    await waitFor(() =>
      expect(
        screen.getByRole('button', { name: 'Remove selected' }),
      ).toHaveFocus(),
    );
    await userEvent.click(
      screen.getByRole('button', { name: 'Clear selection' }),
    );
    await waitFor(() => expect(noticeToast()).not.toBeInTheDocument());
  });

  it('previews mixed eligibility and deletes only explicitly confirmed eligible parts', async () => {
    const deletes = vi.fn().mockImplementation(async () => success());
    setup(
      deletes,
      (ids) =>
        new Response(
          JSON.stringify({
            parts: ids.map((partId) => ({
              partId,
              status: partId === 'a' ? 'Eligible' : 'Blocked',
              legacyJobsLosingRecovery: partId === 'a' ? 2 : 0,
              blockers:
                partId === 'b'
                  ? [
                      {
                        code: 'AssemblyReference',
                        entityId: 'assembly-1',
                        entityType: 'Assembly',
                        name: 'Slab Ass',
                      },
                    ]
                  : [],
            })),
          }),
          { headers: { 'Content-Type': 'application/json' } },
        ),
    );
    await selectAllAndOpen();
    expect(
      await screen.findByRole('link', { name: 'Slab Ass' }),
    ).toHaveAttribute('href', '/assemblies/assembly-1');
    expect(
      screen.getByText(
        '2 finished jobs will permanently lose retry and download.',
      ),
    ).toBeInTheDocument();
    expect(
      within(screen.getByRole('alertdialog')).getByText(
        'These parts will not be removed:',
      ),
    ).toBeInTheDocument();
    expect(deletes).not.toHaveBeenCalled();
    await userEvent.click(
      await screen.findByRole('button', { name: 'Remove part' }),
    );
    await waitFor(() =>
      expect(screen.getByText('1 part removed')).toBeInTheDocument(),
    );
    expect(
      deletes.mock.calls.map(([url]) => new URL(url.toString()).pathname),
    ).toEqual(['/api/v1/parts/a']);
    expect(
      (await screen.findByText('1 part removed')).closest(
        '[data-sonner-toast]',
      ),
    ).toHaveAttribute('data-type', 'success');
    expect(
      (await screen.findByText('Cannot remove Cover')).closest(
        '[data-sonner-toast]',
      ),
    ).toHaveAttribute('data-type', 'warning');
    expect(
      screen.getByRole('checkbox', { name: 'Select Cover', hidden: true }),
    ).toBeChecked();
    expect(
      screen.getByRole('checkbox', { name: 'Select Bracket', hidden: true }),
    ).not.toBeChecked();
  });

  it('stops a batch on permission failure and requires checks for unattempted targets', async () => {
    const deletes = vi
      .fn()
      .mockImplementation(async () => new Response(null, { status: 403 }));
    setup(deletes);
    await selectAllAndOpen();
    await userEvent.click(
      await screen.findByRole('button', { name: 'Remove 2 parts' }),
    );
    expect(
      await screen.findByText('Not removed because access was denied.'),
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(
        document.querySelectorAll(
          '[data-sonner-toast][data-type="error"][data-removed="false"]',
        ),
      ).toHaveLength(2),
    );
    expect(deletes).toHaveBeenCalledOnce();
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Retry failed requests' }),
    ).not.toBeInTheDocument();
  });

  it('selects by ID, shows mixed state, survives sorting/refetch, and prunes vanished IDs', async () => {
    const { onRowClick, rerender } = setup();
    await userEvent.click(
      screen.getByRole('checkbox', { name: 'Select Bracket' }),
    );
    expect(onRowClick).not.toHaveBeenCalled();
    expect(
      screen.getByRole('checkbox', { name: 'Select all parts on this page' }),
    ).toBePartiallyChecked();
    expect(screen.getByRole('status')).toHaveTextContent('1 selected');
    expect(
      screen.getByRole('checkbox', { name: 'Select Bracket' }).closest('tr'),
    ).toHaveAttribute('data-state', 'selected');
    await userEvent.click(screen.getByRole('button', { name: 'Part' }));
    expect(
      screen.getByRole('checkbox', { name: 'Select Bracket' }),
    ).toBeChecked();
    rerender([...parts].reverse());
    expect(
      screen.getByRole('checkbox', { name: 'Select Bracket' }),
    ).toBeChecked();
    expect(
      screen.getByRole('checkbox', { name: 'Select Cover' }),
    ).not.toBeChecked();
    rerender([parts[1]]);
    await waitFor(() =>
      expect(screen.getByRole('status')).toHaveTextContent('0 selected'),
    );
    expect(
      screen.queryByRole('button', { name: 'Remove selected' }),
    ).not.toBeInTheDocument();
  });

  it('shows one floating action region for any selection and restores checkbox focus when cleared', async () => {
    setup();
    expect(
      screen.queryByRole('region', { name: 'Parts actions' }),
    ).not.toBeInTheDocument();
    const checkbox = screen.getByRole('checkbox', { name: 'Select Bracket' });
    await userEvent.click(checkbox);
    const bar = screen.getByRole('region', { name: 'Parts actions' });
    expect(bar).toHaveTextContent('1 selected');
    expect(
      within(bar).getByRole('button', { name: 'Remove selected' }),
    ).toBeEnabled();
    expect(checkbox).toHaveFocus();
    await userEvent.click(
      screen.getByRole('checkbox', { name: 'Select Cover' }),
    );
    expect(bar).toHaveTextContent('2 selected');
    expect(
      screen.getAllByRole('region', { name: 'Parts actions' }),
    ).toHaveLength(1);
    await userEvent.click(
      within(bar).getByRole('button', { name: 'Clear selection' }),
    );
    expect(
      screen.queryByRole('region', { name: 'Parts actions' }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole('checkbox', { name: 'Select all parts on this page' }),
    ).toHaveFocus();
    expect(checkbox).not.toBeChecked();
  });

  it('clears selection when the server-page/filter context changes', async () => {
    const { rerender } = setup();
    await userEvent.click(
      screen.getByRole('checkbox', { name: 'Select all parts on this page' }),
    );
    expect(
      screen.getByRole('checkbox', { name: 'Select Cover' }),
    ).toBeChecked();
    rerender(parts, 'page-2');
    expect(
      screen.getByRole('checkbox', { name: 'Select Bracket' }),
    ).not.toBeChecked();
    expect(screen.getByRole('status')).toHaveTextContent('0 selected');
  });

  it('cancels a named bulk confirmation without requests and returns focus', async () => {
    const fetchMock = vi.fn();
    setup(fetchMock);
    await selectAllAndOpen();
    const dialog = await screen.findByRole('alertdialog');
    expect(within(dialog).getByText('Bracket')).toBeInTheDocument();
    expect(within(dialog).getByText('Cover')).toBeInTheDocument();
    await userEvent.click(
      within(dialog).getByRole('button', { name: 'Cancel' }),
    );
    expect(fetchMock).not.toHaveBeenCalled();
    await waitFor(() =>
      expect(
        screen.getByRole('button', { name: 'Remove selected' }),
      ).toHaveFocus(),
    );
  });

  it('removes all selected parts, clears detail caches, and refreshes once', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => success());
    const { client } = setup(fetchMock);
    const invalidate = vi.spyOn(client, 'invalidateQueries');
    await selectAllAndOpen();
    await userEvent.click(
      await within(screen.getByRole('alertdialog')).findByRole('button', {
        name: 'Remove 2 parts',
      }),
    );
    await waitFor(() =>
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument(),
    );
    expect(
      fetchMock.mock.calls.map(
        ([url]) => new URL(url.toString(), 'http://localhost').pathname,
      ),
    ).toEqual(['/api/v1/parts/a', '/api/v1/parts/b']);
    expect(client.getQueryData(['/api/v1/parts/a'])).toBeUndefined();
    expect(client.getQueryData(['/api/v1/parts/b'])).toBeUndefined();
    expect(invalidate).toHaveBeenCalledOnce();
    expect(screen.getByRole('status')).toHaveTextContent('0 selected');
  });

  it('retains blocked selection and rechecks before another confirmation', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(success())
      .mockResolvedValueOnce(failure())
      .mockResolvedValueOnce(success());
    const { client } = setup(fetchMock);
    const invalidate = vi.spyOn(client, 'invalidateQueries');
    await selectAllAndOpen();
    await userEvent.click(
      await within(screen.getByRole('alertdialog')).findByRole('button', {
        name: 'Remove 2 parts',
      }),
    );
    expect(
      await screen.findByText('Referenced by assembly Enclosure.'),
    ).toBeInTheDocument();
    expect(screen.getByText('1 part removed')).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Retry failed requests' }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole('checkbox', { name: 'Select Bracket', hidden: true }),
    ).not.toBeChecked();
    expect(
      screen.getByRole('checkbox', { name: 'Select Cover', hidden: true }),
    ).toBeChecked();
    expect(client.getQueryData(['/api/v1/parts/b'])).toBeDefined();
    expect(invalidate).toHaveBeenCalledOnce();
    await userEvent.click(
      screen.getByRole('button', { name: 'Remove selected' }),
    );
    await userEvent.click(
      await screen.findByRole('button', { name: 'Remove part' }),
    );
    await waitFor(() =>
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument(),
    );
    expect(
      fetchMock.mock.calls.map(
        ([url]) => new URL(url.toString(), 'http://localhost').pathname,
      ),
    ).toEqual(['/api/v1/parts/a', '/api/v1/parts/b', '/api/v1/parts/b']);
  });

  it('keeps dependency conflicts blocked without invalidating unchanged lists', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => failure());
    const { client } = setup(fetchMock);
    const invalidate = vi.spyOn(client, 'invalidateQueries');
    await selectAllAndOpen();
    await userEvent.click(
      await within(screen.getByRole('alertdialog')).findByRole('button', {
        name: 'Remove 2 parts',
      }),
    );
    await waitFor(() =>
      expect(
        screen.getAllByText('Referenced by assembly Enclosure.'),
      ).toHaveLength(2),
    );
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(invalidate).not.toHaveBeenCalled();
  });

  it('keeps the dialog mounted and blocks repeat requests and conflicting actions while pending', async () => {
    let finish: (value: Response) => void = () => {};
    const fetchMock = vi
      .fn()
      .mockImplementationOnce(
        () =>
          new Promise<Response>((resolve) => {
            finish = resolve;
          }),
      )
      .mockResolvedValueOnce(success());
    const { onPendingChange } = setup(fetchMock);
    await selectAllAndOpen();
    await userEvent.click(
      await within(screen.getByRole('alertdialog')).findByRole('button', {
        name: 'Remove 2 parts',
      }),
    );
    expect(screen.getByRole('button', { name: 'Removing…' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
    expect(
      screen.getByRole('button', { name: 'Actions for Bracket', hidden: true }),
    ).toBeDisabled();
    expect(
      screen.getByRole('checkbox', { name: 'Select Cover', hidden: true }),
    ).toBeDisabled();
    await userEvent.keyboard('{Escape}{Enter}');
    expect(fetchMock).toHaveBeenCalledOnce();
    await act(async () => finish(success()));
    await waitFor(() =>
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument(),
    );
    expect(onPendingChange.mock.calls.map(([value]) => value)).toEqual([
      true,
      false,
      true,
      false,
    ]);
  });

  it('opens a row menu with the keyboard and restores focus after cancelling removal', async () => {
    const { onRowClick } = setup();
    const trigger = screen.getByRole('button', { name: 'Actions for Bracket' });
    trigger.focus();
    await userEvent.keyboard('{Enter}');
    await userEvent.click(screen.getByRole('menuitem', { name: 'Remove' }));
    await userEvent.click(
      await screen.findByRole('button', { name: 'Cancel' }),
    );
    await waitFor(() => expect(trigger).toHaveFocus());
    expect(onRowClick).not.toHaveBeenCalled();
  });

  it('keeps a single-row dependency failure open for retry across pending updates', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(failure())
      .mockResolvedValueOnce(success());
    setup(fetchMock);
    await userEvent.click(
      screen.getByRole('button', { name: 'Actions for Bracket' }),
    );
    await userEvent.click(screen.getByRole('menuitem', { name: 'Remove' }));
    await userEvent.click(
      await within(screen.getByRole('alertdialog')).findByRole('button', {
        name: 'Remove part',
      }),
    );
    expect(
      await screen.findByText('Referenced by assembly Enclosure.'),
    ).toBeInTheDocument();
    await userEvent.click(
      screen.getByRole('button', { name: 'Actions for Bracket' }),
    );
    await userEvent.click(screen.getByRole('menuitem', { name: 'Remove' }));
    await userEvent.click(
      await within(screen.getByRole('alertdialog')).findByRole('button', {
        name: 'Remove part',
      }),
    );
    await waitFor(() =>
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument(),
    );
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
});
