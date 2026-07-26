import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { createMemoryRouter, Link, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useUnsavedChangesPrompt } from './useUnsavedChangesPrompt';

function TestPage({ initialDirty }: { initialDirty: boolean }) {
  const [isDirty, setIsDirty] = useState(initialDirty);
  const { ConfirmLeaveDialog } = useUnsavedChangesPrompt(isDirty);
  return (
    <div>
      <p>Page A</p>
      <button type="button" onClick={() => setIsDirty(false)}>
        Mark clean
      </button>
      <Link to="/b">Go to B</Link>
      <ConfirmLeaveDialog />
    </div>
  );
}

function renderRouter(initialDirty: boolean) {
  const router = createMemoryRouter(
    [
      { path: '/a', element: <TestPage initialDirty={initialDirty} /> },
      { path: '/b', element: <div>Page B</div> },
    ],
    { initialEntries: ['/a'] },
  );
  render(<RouterProvider router={router} />);
  return router;
}

describe('useUnsavedChangesPrompt', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('blocks in-app navigation while dirty and only proceeds on confirm', async () => {
    const user = userEvent.setup();
    renderRouter(true);

    expect(screen.getByText('Page A')).toBeInTheDocument();
    await user.click(screen.getByRole('link', { name: 'Go to B' }));

    const dialog = await screen.findByRole('alertdialog');
    expect(
      within(dialog).getByText('Discard unsaved changes?'),
    ).toBeInTheDocument();

    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(screen.getByText('Page A')).toBeInTheDocument();

    await user.click(screen.getByRole('link', { name: 'Go to B' }));
    const dialog2 = await screen.findByRole('alertdialog');
    await user.click(
      within(dialog2).getByRole('button', { name: 'Discard changes' }),
    );

    expect(await screen.findByText('Page B')).toBeInTheDocument();
  });

  it('does not block navigation when isDirty is false', async () => {
    const user = userEvent.setup();
    renderRouter(false);

    await user.click(screen.getByRole('link', { name: 'Go to B' }));

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(await screen.findByText('Page B')).toBeInTheDocument();
  });

  it('attaches the beforeunload listener only while isDirty is true', async () => {
    const user = userEvent.setup();
    const addSpy = vi.spyOn(window, 'addEventListener');
    const removeSpy = vi.spyOn(window, 'removeEventListener');

    renderRouter(true);

    expect(addSpy).toHaveBeenCalledWith('beforeunload', expect.any(Function));

    await user.click(screen.getByRole('button', { name: 'Mark clean' }));

    expect(removeSpy).toHaveBeenCalledWith(
      'beforeunload',
      expect.any(Function),
    );
  });
});
