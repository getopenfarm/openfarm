import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { toast } from 'sonner';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { RemovePartButton } from './RemovePartButton';

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}
function preview(status = 'Eligible') {
  return json({
    parts: [
      {
        partId: 'part-1',
        status,
        blockers:
          status === 'Blocked'
            ? [
                {
                  code: 'AssemblyReference',
                  entityId: 'assembly-1',
                  entityType: 'Assembly',
                  name: 'Enclosure',
                },
              ]
            : [],
        legacyJobsLosingRecovery: 0,
      },
    ],
  });
}
function noticeToast() {
  return document.querySelector(
    '[data-sonner-toast][data-removed="false"]:is([data-type="warning"], [data-type="error"], [data-type="info"])',
  );
}
async function findNoticeToast() {
  return waitFor(() => {
    const node = noticeToast();
    if (!node) throw new Error('Waiting for removal toast');
    return node;
  });
}

function setup(
  handler: (url: string, init?: RequestInit) => Promise<Response> | Response,
) {
  const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) =>
    handler(input.toString(), init),
  );
  vi.stubGlobal('fetch', fetchMock);
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  client.setQueryData(['/api/v1/parts/part-1'], { data: { name: 'Bracket' } });
  const removed = vi.fn();
  const rowClick = vi.fn();
  const view = render(
    <QueryClientProvider client={client}>
      <table>
        <tbody>
          <tr onClick={rowClick}>
            <td>
              <RemovePartButton
                partId="part-1"
                partName="Bracket"
                onRemoved={removed}
              />
            </td>
          </tr>
        </tbody>
      </table>
      <Toaster />
    </QueryClientProvider>,
  );
  return { client, removed, rowClick, fetchMock, unmount: view.unmount };
}
async function open() {
  await userEvent.click(screen.getByRole('button', { name: 'Remove' }));
}
async function confirm() {
  await userEvent.click(
    await screen.findByRole('button', { name: 'Remove part' }),
  );
}
afterEach(() => {
  toast.dismiss();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('RemovePartButton eligibility pipeline', () => {
  it('checks on open, cancels without DELETE, and restores keyboard focus', async () => {
    const { fetchMock, rowClick } = setup(() => preview());
    screen.getByRole('button', { name: 'Remove' }).focus();
    await userEvent.keyboard('{Enter}');
    expect(
      await screen.findByRole('button', { name: 'Remove part' }),
    ).toBeEnabled();
    expect(rowClick).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(
      fetchMock.mock.calls.every(([, init]) => init?.method === 'POST'),
    ).toBe(true);
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Remove' })).toHaveFocus(),
    );
  });
  it('shows blockers and links without offering a delete retry', async () => {
    const { fetchMock } = setup(() => preview('Blocked'));
    await open();
    expect(
      await screen.findByRole('link', { name: 'Enclosure' }),
    ).toHaveAttribute('href', '/assemblies/assembly-1');
    expect(
      await screen.findByText('Cannot remove Bracket'),
    ).toBeInTheDocument();
    expect(noticeToast()).toHaveAttribute('data-type', 'warning');
    expect(
      noticeToast()?.querySelector('[data-description]'),
    ).not.toHaveTextContent('Bracket');
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Cancel' }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Retry|Check again/ }),
    ).not.toBeInTheDocument();
    expect(noticeToast()).not.toHaveTextContent(
      /0 removed|0 ready|snapshots|eligible/,
    );
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Remove' })).toHaveFocus(),
    );
    screen.getByRole('button', { name: 'Close toast' }).focus();
    await userEvent.keyboard('{Enter}');
    await waitFor(() => expect(noticeToast()).not.toBeInTheDocument());
    await open();
    expect(await findNoticeToast()).toHaveTextContent('Enclosure');
    expect(
      document.querySelectorAll(
        '[data-sonner-toast][data-removed="false"]:is([data-type="warning"], [data-type="error"], [data-type="info"])',
      ),
    ).toHaveLength(1);
    expect(
      fetchMock.mock.calls.every(([, init]) => init?.method === 'POST'),
    ).toBe(true);
  });
  it('requires reopening after check errors without exposing pipeline controls', async () => {
    let failed = true;
    const { fetchMock } = setup(() =>
      failed ? json({ detail: 'Offline' }, 503) : preview(),
    );
    await open();
    expect(await findNoticeToast()).toHaveTextContent(
      'Couldn’t check this part.',
    );
    expect(noticeToast()).toHaveAttribute('data-type', 'error');
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    failed = false;
    expect(
      screen.queryByRole('button', { name: /Retry|Check again/ }),
    ).not.toBeInTheDocument();
    await open();
    expect(
      await screen.findByRole('button', { name: 'Remove part' }),
    ).toBeEnabled();
    expect(
      fetchMock.mock.calls.every(([, init]) => init?.method === 'POST'),
    ).toBe(true);
  });
  it('prevents duplicate submissions and closing during DELETE', async () => {
    let finish: (response: Response) => void = () => {};
    const { fetchMock, removed } = setup((_, init) =>
      init?.method === 'POST'
        ? preview()
        : new Promise((resolve) => {
            finish = resolve;
          }),
    );
    await open();
    await confirm();
    expect(screen.getByRole('button', { name: 'Removing…' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
    await userEvent.keyboard('{Escape}{Enter}');
    expect(
      fetchMock.mock.calls.filter(([, init]) => init?.method === 'DELETE'),
    ).toHaveLength(1);
    await act(async () => finish(new Response(null, { status: 204 })));
    await waitFor(() => expect(removed).toHaveBeenCalledOnce());
  });
  it('rechecks a network failure and requires a separate confirmation', async () => {
    let fail = true;
    const { fetchMock, removed } = setup((_, init) => {
      if (init?.method === 'POST') return preview();
      if (fail) return Promise.reject(new TypeError('Lost response'));
      return new Response(null, { status: 204 });
    });
    await open();
    await confirm();
    expect(
      await screen.findByText('Removal could not be confirmed. Try again.'),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Retry|Check again/ }),
    ).not.toBeInTheDocument();
    await open();
    expect(
      await screen.findByRole('button', { name: 'Remove part' }),
    ).toBeEnabled();
    expect(
      fetchMock.mock.calls.filter(([, init]) => init?.method === 'DELETE'),
    ).toHaveLength(1);
    fail = false;
    await confirm();
    await waitFor(() => expect(removed).toHaveBeenCalledOnce());
  });
  it('reconciles a lost success as absent without claiming a removal or repeating DELETE', async () => {
    let deleted = false;
    const { fetchMock, client, removed } = setup((_, init) => {
      if (init?.method === 'POST')
        return preview(deleted ? 'Absent' : 'Eligible');
      deleted = true;
      return Promise.reject(new TypeError('Lost response'));
    });
    await open();
    await confirm();
    expect(
      await screen.findByText('Removal could not be confirmed. Try again.'),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Retry|Check again/ }),
    ).not.toBeInTheDocument();
    await open();
    expect(
      await screen.findByText('Already removed from the library.'),
    ).toBeInTheDocument();
    expect(await findNoticeToast()).not.toHaveTextContent('part removed.');
    expect(client.getQueryData(['/api/v1/parts/part-1'])).toBeUndefined();
    expect(removed).toHaveBeenCalledOnce();
    expect(
      fetchMock.mock.calls.filter(([, init]) => init?.method === 'DELETE'),
    ).toHaveLength(1);
  });
  it('preserves committed success when refreshing queries fails', async () => {
    const { client, removed } = setup((_, init) =>
      init?.method === 'POST' ? preview() : new Response(null, { status: 204 }),
    );
    vi.spyOn(client, 'invalidateQueries').mockRejectedValue(
      new Error('Refresh failed'),
    );
    await open();
    await confirm();
    expect(await findNoticeToast()).toHaveTextContent(
      'The list could not be refreshed',
    );
    expect(screen.getByText('1 part removed')).toBeInTheDocument();
    expect(removed).toHaveBeenCalledOnce();
    expect(
      screen.queryByRole('button', { name: 'Retry failed requests' }),
    ).not.toBeInTheDocument();
  });
  it('shows preflight in a toast and prevents repeated requests while checking', async () => {
    let finish: (response: Response) => void = () => {};
    const { fetchMock } = setup(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    await open();
    expect(await screen.findByText('Checking parts…')).toBeInTheDocument();
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Remove' })).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: 'Remove' }));
    expect(fetchMock).toHaveBeenCalledOnce();
    await act(async () => finish(preview('Blocked')));
    expect(await findNoticeToast()).toHaveTextContent('Enclosure');
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });
  it('ignores a cancelled preflight after unmounting and starting another attempt', async () => {
    let finish: (response: Response) => void = () => {};
    const first = setup(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    await open();
    first.unmount();
    expect(first.fetchMock.mock.calls[0][1]?.signal?.aborted).toBe(true);
    setup(() => preview('Blocked'));
    await open();
    expect(await findNoticeToast()).toHaveTextContent('Enclosure');
    await act(async () => finish(preview()));
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(noticeToast()).toHaveTextContent('Enclosure');
    expect(first.removed).not.toHaveBeenCalled();
  });
});
