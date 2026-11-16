import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { downloadFile } from '@/lib/api/downloadFile';
import { AccessContext } from '@/lib/auth/AccessContext';
import { PartFileActions } from './PartFileActions';

vi.mock('@/lib/api/downloadFile', () => ({ downloadFile: vi.fn() }));
function setup(permissions = ['parts:read', 'parts:replace-file']) {
  const submit = vi.fn((event: React.FormEvent) => event.preventDefault());
  vi.stubGlobal(
    'fetch',
    vi.fn(() =>
      Promise.resolve(
        new Response(JSON.stringify({ items: [] }), {
          headers: { 'Content-Type': 'application/json' },
        }),
      ),
    ),
  );
  render(
    <AccessContext.Provider value={new Set(permissions)}>
      <QueryClientProvider
        client={
          new QueryClient({ defaultOptions: { queries: { retry: false } } })
        }
      >
        <form onSubmit={submit}>
          <PartFileActions
            part={{
              id: 'part-1',
              name: 'Bracket',
              originalFileName: 'plates.3mf',
              printRequirementsStatus: 'Incomplete',
            }}
          />
        </form>
      </QueryClientProvider>
    </AccessContext.Provider>,
  );
  return { user: userEvent.setup(), submit };
}
afterEach(() => {
  vi.unstubAllGlobals();
  vi.clearAllMocks();
});

describe('PartFileActions', () => {
  it('downloads incomplete parts once while pending and permits another download afterward', async () => {
    let finish!: () => void;
    vi.mocked(downloadFile).mockImplementationOnce(
      () =>
        new Promise<void>((resolve) => {
          finish = resolve;
        }),
    );
    const { user, submit } = setup();
    await user.click(screen.getByRole('button', { name: 'File actions' }));
    await user.click(screen.getByRole('menuitem', { name: 'Download file' }));
    expect(
      screen.getByRole('button', { name: 'File actions' }),
    ).toHaveAttribute('aria-busy', 'true');
    await user.click(screen.getByRole('button', { name: 'File actions' }));
    const pending = screen.getByRole('menuitem', { name: 'Downloading…' });
    expect(pending).toHaveAttribute('aria-disabled', 'true');
    await user.click(pending);
    expect(downloadFile).toHaveBeenCalledTimes(1);
    expect(downloadFile).toHaveBeenCalledWith(
      '/api/v1/parts/part-1/file',
      'plates.3mf',
      'Could not download part file.',
    );
    await act(async () => finish());
    await user.click(screen.getByRole('menuitem', { name: 'Download file' }));
    expect(downloadFile).toHaveBeenCalledTimes(2);
    expect(submit).not.toHaveBeenCalled();
  });
  it('keeps download available to readers and hides maintenance actions', async () => {
    const { user } = setup(['parts:read']);
    await user.click(screen.getByRole('button', { name: 'File actions' }));
    expect(
      screen.getByRole('menuitem', { name: 'Download file' }),
    ).toBeVisible();
    expect(
      screen.queryByRole('menuitem', { name: 'Replace file' }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('menuitem', { name: 'Reprocess' }),
    ).not.toBeInTheDocument();
  });
  it('requires read permission to expose download', async () => {
    const { user } = setup(['parts:replace-file']);
    await user.click(screen.getByRole('button', { name: 'File actions' }));
    expect(
      screen.queryByRole('menuitem', { name: 'Download file' }),
    ).not.toBeInTheDocument();
  });
  it('hides the menu when no file actions are permitted', () => {
    setup([]);
    expect(
      screen.queryByRole('button', { name: 'File actions' }),
    ).not.toBeInTheDocument();
  });
  it.each([
    ['Replace file', 'Replace file for Bracket', '{Home}{ArrowDown}'],
    ['Reprocess', 'Reprocess part for Bracket', '{End}'],
  ])(
    'opens %s by keyboard and returns focus on cancellation',
    async (_label, title, key) => {
      const { user, submit } = setup();
      const trigger = screen.getByRole('button', { name: 'File actions' });
      trigger.focus();
      await user.keyboard('{ArrowDown}');
      await screen.findByRole('menu');
      await user.keyboard(key);
      await user.keyboard('{Enter}');
      expect(await screen.findByRole('dialog', { name: title })).toBeVisible();
      expect(screen.queryByRole('menu')).not.toBeInTheDocument();
      await user.keyboard('{Escape}');
      await waitFor(() =>
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument(),
      );
      await waitFor(() => expect(trigger).toHaveFocus());
      expect(submit).not.toHaveBeenCalled();
    },
  );
  it('dismisses the file menu with Escape and restores focus', async () => {
    const { user } = setup();
    const trigger = screen.getByRole('button', { name: 'File actions' });
    trigger.focus();
    await user.keyboard('{ArrowDown}');
    expect(await screen.findByRole('menu')).toBeVisible();
    await user.keyboard('{Escape}');
    await waitFor(() => expect(trigger).toHaveFocus());
  });
});
