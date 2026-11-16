import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { AuditEntryResponse } from '@/lib/api/generated/endpoints.schemas';
import { AuditEntryPage } from './AuditEntryPage';
import { AuditLogPage } from './AuditLogPage';

const entry = {
  id: 'event-1',
  entityType: 'Printer',
  entityId: 'printer-1',
  entityLabel: 'Workshop printer',
  eventType: 'PrinterEdited',
  action: 'update',
  actorId: 'person-1',
  actorName: 'Alex',
  channel: 'Token',
  credentialId: 'token-1',
  credentialLabel: 'Farm assistant',
  correlationId: 'request-1',
  operation: 'rename_printer',
  timestampUtc: '2026-11-15T03:00:00Z',
  changes: [
    { field: 'Name', before: 'Original printer', after: 'Workshop printer' },
  ],
  href: '/printers/printer-1',
};
function response(data: unknown) {
  return new Response(JSON.stringify(data), {
    headers: { 'Content-Type': 'application/json' },
  });
}
function setup(
  path = '/audit-log',
  fail = false,
  override: AuditEntryResponse = {},
  relatedItems: AuditEntryResponse[] = [],
) {
  const currentEntry = { ...entry, ...override };
  const fetch = vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input);
    if (fail) return new Response(null, { status: 500 });
    if (url.includes('filter-options'))
      return response({
        actors: [{ id: 'person-1', name: 'Alex' }],
        credentials: [
          { id: 'token-1', label: 'Farm assistant', channel: 'Token' },
        ],
        entityTypes: ['Printer'],
        actions: ['update'],
      });
    if (url.endsWith('/related'))
      return response({ items: relatedItems, totalCount: relatedItems.length });
    if (url.includes('/event-1')) return response(currentEntry);
    return response({
      items: [currentEntry],
      totalCount: 80,
      page: 1,
      pageSize: 25,
    });
  });
  vi.stubGlobal('fetch', fetch);
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const router = createMemoryRouter(
    [
      { path: '/audit-log', element: <AuditLogPage /> },
      { path: '/audit-log/:id', element: <AuditEntryPage /> },
    ],
    { initialEntries: [path] },
  );
  const view = render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
  return { fetch, router, ...view };
}

afterEach(() => vi.unstubAllGlobals());

describe('Audit log', () => {
  it('shows names, channel, changes and real detail links with numbered pagination', async () => {
    setup();
    expect(await screen.findByText('Workshop printer')).toBeInTheDocument();
    expect(screen.getByText('Alex')).toBeInTheDocument();
    expect(screen.getByText('Farm assistant')).toBeInTheDocument();
    expect(screen.getByText('1–25 of 80')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Nov.*2026/ })).toHaveAttribute(
      'href',
      '/audit-log/event-1',
    );
  });

  it('supports keyboard filtering, resets page and keeps shareable filters', async () => {
    const user = userEvent.setup();
    const { router, fetch } = setup('/audit-log?page=3');
    await screen.findByText('Workshop printer');
    const channel = screen.getByRole('button', {
      name: 'Channel',
    });
    channel.focus();
    await user.keyboard('{Enter}');
    await screen.findByRole('menuitemradio', { name: 'Access token' });
    await user.keyboard('a{Enter}');
    expect(screen.getByRole('menu')).toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(channel).toHaveFocus();
    await waitFor(() =>
      expect(router.state.location.search).toContain('channel=Token'),
    );
    expect(router.state.location.search).not.toContain('page=3');
    expect(
      fetch.mock.calls.some(([url]) => String(url).includes('channel=Token')),
    ).toBe(true);
    await user.click(screen.getByRole('button', { name: 'Clear filters' }));
    expect(router.state.location.search).toBe('');
  });

  it('opens details using a keyboard link and returns to the filtered list', async () => {
    const user = userEvent.setup();
    setup('/audit-log?entityType=Printer');
    const link = await screen.findByRole('link', { name: /Nov.*2026/ });
    link.focus();
    await user.keyboard('{Enter}');
    expect(await screen.findByText('Original printer')).toBeInTheDocument();
    expect(
      screen.getByText('Workshop printer', { selector: 'td' }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('link', { name: /Open this record/ }),
    ).toHaveAttribute('href', '/printers/printer-1');
    expect(screen.getByRole('link', { name: 'Audit log' })).toHaveAttribute(
      'href',
      '/audit-log?entityType=Printer',
    );
  });

  it('sends complete local days using an exclusive next-day boundary', async () => {
    const { fetch } = setup('/audit-log?from=2026-11-15&to=2026-11-15');
    await screen.findByText('Workshop printer');
    const call = fetch.mock.calls.find(([input]) =>
      String(input).includes('/audit-log?'),
    );
    expect(call).toBeDefined();
    const url = new URL(String(call?.[0]), 'http://localhost');
    expect(url.searchParams.get('from')).toBe(
      new Date(2026, 10, 15).toISOString(),
    );
    expect(url.searchParams.get('to')).toBe(
      new Date(2026, 10, 16).toISOString(),
    );
  });

  it('keeps dates collapsed and groups record type and action in one menu', async () => {
    const user = userEvent.setup();
    const { router } = setup();
    await screen.findByText('Workshop printer');
    expect(
      screen.queryByLabelText('From', { exact: true }),
    ).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Type' }));
    await user.click(screen.getByRole('menuitemradio', { name: 'Printer' }));
    await user.click(screen.getByRole('menuitemradio', { name: 'Changed' }));
    expect(router.state.location.search).toContain('entityType=Printer');
    expect(router.state.location.search).toContain('action=update');
    await user.keyboard('{Escape}');
    const dateButton = screen.getByRole('button', {
      name: 'Date',
    });
    await user.click(dateButton);
    expect(screen.getByLabelText('From', { exact: true })).toBeInTheDocument();
    expect(screen.getByLabelText('To', { exact: true })).toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(dateButton).toHaveFocus();
  });

  it('preserves independent view pages and keyboard tab navigation', async () => {
    const user = userEvent.setup();
    const { router } = setup('/audit-log?page=3');
    await screen.findByText('Workshop printer');
    const everything = screen.getByRole('tab', { name: 'Everything' });
    everything.focus();
    await user.keyboard('{ArrowRight}');
    await waitFor(() =>
      expect(router.state.location.search).toContain('channel=Agents'),
    );
    expect(router.state.location.search).toContain('page=1');
    await waitFor(() =>
      expect(
        screen.getByRole('tab', { name: 'Tokens and apps' }),
      ).toHaveAttribute('aria-selected', 'true'),
    );
    await waitFor(() =>
      expect(
        screen.getByRole('button', { name: 'Next audit log page' }),
      ).toBeEnabled(),
    );
    await user.click(
      screen.getByRole('button', { name: 'Next audit log page' }),
    );
    await waitFor(() =>
      expect(router.state.location.search).toContain('page=2'),
    );
    await user.click(everything);
    await waitFor(() =>
      expect(router.state.location.search).toContain('page=3'),
    );
    expect(router.state.location.search).not.toContain('channel=Agents');
    await user.click(screen.getByRole('tab', { name: 'Tokens and apps' }));
    expect(router.state.location.search).toContain('page=2');
    await waitFor(() =>
      expect(
        screen.getByRole('combobox', { name: 'Rows per page' }),
      ).toBeEnabled(),
    );
    await user.click(screen.getByRole('combobox', { name: 'Rows per page' }));
    await user.click(screen.getByRole('option', { name: '50' }));
    await waitFor(() =>
      expect(router.state.location.search).toContain('pageSize=50'),
    );
    expect(router.state.location.search).toContain('page=1');
    await user.click(everything);
    await waitFor(() =>
      expect(router.state.location.search).toContain('page=3'),
    );
    expect(router.state.location.search).toContain('pageSize=25');
    await user.click(screen.getByRole('tab', { name: 'Tokens and apps' }));
    expect(router.state.location.search).toContain('pageSize=50');
    expect(router.state.location.search).toContain('page=1');
    await user.click(screen.getByRole('button', { name: 'Clear filters' }));
    await waitFor(() =>
      expect(everything).toHaveAttribute('aria-selected', 'true'),
    );
    expect(
      screen.getByRole('combobox', { name: 'Rows per page' }),
    ).toHaveTextContent('25');
    await user.click(screen.getByRole('tab', { name: 'Tokens and apps' }));
    expect(router.state.location.search).toContain('pageSize=50');
  });

  it('uses the framed Fields control and retains field choices across views', async () => {
    const user = userEvent.setup();
    setup();
    await screen.findByText('Workshop printer');
    const fields = screen.getByRole('button', { name: 'Fields' });
    expect(
      fields.closest('[data-slot="table-toolbar-row"]'),
    ).toBeInTheDocument();
    await user.click(fields);
    await user.click(screen.getByRole('menuitemcheckbox', { name: 'Who' }));
    await user.keyboard('{Escape}');
    expect(
      screen.queryByRole('columnheader', { name: 'Who' }),
    ).not.toBeInTheDocument();
    await user.click(screen.getByRole('tab', { name: 'Tokens and apps' }));
    expect(
      screen.queryByRole('columnheader', { name: 'Who' }),
    ).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Nov.*2026/ })).toBeInTheDocument();
  });

  it('shows value changes and distinguishes agent rows without a browser badge', async () => {
    setup();
    const what = (await screen.findByText('Workshop printer')).closest('td');
    expect(what).toBeInTheDocument();
    expect(
      screen.getByText('Name Original printer → Workshop printer'),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('row', { name: /Alex.*Workshop printer/ }),
    ).toHaveAttribute('data-agent', 'true');
  });

  it('keeps signed-in rows quiet and suppresses empty related-request cards', async () => {
    const user = userEvent.setup();
    setup('/audit-log', false, {
      channel: 'Browser',
      credentialLabel: undefined,
    });
    await screen.findByText('Workshop printer');
    expect(screen.queryByText('Signed in')).not.toBeInTheDocument();
    await user.click(screen.getByRole('link', { name: /Nov.*2026/ }));
    expect(await screen.findByText('Original printer')).toBeInTheDocument();
    expect(screen.getByText('The printer')).toBeInTheDocument();
    await waitFor(() =>
      expect(
        screen.queryByText('Changed in the same request'),
      ).not.toBeInTheDocument(),
    );
  });

  it('expands related changes without losing the filtered return link', async () => {
    const user = userEvent.setup();
    const relatedItems = Array.from({ length: 5 }, (_, index) => ({
      ...entry,
      id: `related-${index}`,
      entityLabel: `Related printer ${index}`,
    }));
    setup('/audit-log/event-1', false, {}, relatedItems);
    await screen.findByRole('link', { name: /Related printer 0/ });
    expect(
      screen.queryByRole('link', { name: /Related printer 4/ }),
    ).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Show 2 more' }));
    expect(
      screen.getByRole('link', { name: /Related printer 4/ }),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Show less' })).toHaveAttribute(
      'aria-expanded',
      'true',
    );
  });

  it('uses a single Value column for created records', async () => {
    setup('/audit-log/event-1', false, {
      action: 'create',
      changes: [{ field: 'Name', before: null, after: 'Workshop printer' }],
    });
    expect(
      await screen.findByRole('columnheader', { name: 'Value' }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('columnheader', { name: 'Before' }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('columnheader', { name: 'After' }),
    ).not.toBeInTheDocument();
  });

  it('shows permission additions and removals as readable changes', async () => {
    setup('/audit-log/event-1', false, {
      changes: [
        {
          field: 'Permissions',
          before: ['printers:list'],
          after: ['spools:list'],
        },
      ],
    });
    expect(await screen.findByText('+ spools:list')).toBeInTheDocument();
    expect(screen.getByText('− printers:list')).toBeInTheDocument();
    expect(
      screen.getByRole('columnheader', { name: 'Before' }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('columnheader', { name: 'After' }),
    ).toBeInTheDocument();
  });

  it('clears one filter section and restores focus to its trigger', async () => {
    const user = userEvent.setup();
    const { router } = setup(
      '/audit-log?entityType=Printer&action=update&from=2026-11-15&to=2026-11-15',
    );
    await screen.findByText('Workshop printer');
    const type = screen.getByRole('button', { name: 'Type' });
    await user.click(type);
    await user.click(
      screen.getByRole('menuitem', { name: 'Clear what changed filter' }),
    );
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(type).toHaveFocus();
    expect(router.state.location.search).not.toContain('entityType');
    expect(router.state.location.search).toContain('action=update');
    const date = screen.getByRole('button', { name: 'Date' });
    await user.click(date);
    await user.click(screen.getByRole('button', { name: 'Clear dates' }));
    expect(date).toHaveFocus();
    expect(router.state.location.search).not.toContain('from=');
    expect(router.state.location.search).not.toContain('to=');
  });

  it('does not add browser Back steps for filter adjustments', async () => {
    const user = userEvent.setup();
    const { router } = setup();
    await screen.findByText('Workshop printer');
    await user.click(screen.getByRole('button', { name: 'Person' }));
    await user.click(screen.getByRole('menuitemradio', { name: 'Alex' }));
    await user.keyboard('{Escape}');
    await user.click(screen.getByRole('link', { name: /Nov.*2026/ }));
    await screen.findByText('Original printer');
    await act(async () => {
      await router.navigate(-1);
    });
    await screen.findByText('Workshop printer');
    expect(router.state.location.search).toContain('actorId=person-1');
    await act(async () => {
      await router.navigate(-1);
    });
    expect(router.state.location.search).toContain('actorId=person-1');
  });

  it('retains a retry path when the audit request fails', async () => {
    setup('/audit-log', true);
    expect(
      await screen.findByText('Could not load audit log.'),
    ).toBeInTheDocument();
    expect(
      screen.getAllByRole('button', { name: 'Retry' }).length,
    ).toBeGreaterThan(0);
  });
});
