import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it } from 'vitest';
import { AuthProvider } from '@/lib/auth/AuthProvider';
import { clearToken, setToken } from '@/lib/auth/tokenStorage';
import { AppShell } from './AppShell';

function renderShell(initialPath = '/') {
  setToken('a-valid-token');
  const testRouter = createMemoryRouter(
    [
      { path: '/login', element: <div>Login screen</div> },
      {
        element: <AppShell />,
        children: [
          {
            path: '/',
            element: <div>Dashboard content</div>,
            handle: { title: 'Dashboard' },
          },
          {
            path: '/activity-log',
            element: <div>Activity log content</div>,
            handle: { title: 'Activity Log' },
          },
        ],
      },
    ],
    { initialEntries: [initialPath] },
  );
  return render(
    <AuthProvider>
      <RouterProvider router={testRouter} />
    </AuthProvider>,
  );
}

describe('AppShell', () => {
  afterEach(() => {
    clearToken();
  });

  it('renders the grouped nav sections', () => {
    renderShell();

    expect(screen.getByRole('link', { name: 'Dashboard' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Printers' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Jobs' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Filament' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Parts' })).toBeInTheDocument();
    expect(
      screen.getByRole('link', { name: 'Assemblies' }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('link', { name: 'Maintenance' }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('link', { name: 'Activity Log' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Settings' })).toBeInTheDocument();
  });

  it('shows the current route title in the title bar', () => {
    renderShell('/activity-log');

    expect(
      screen.getByRole('heading', { name: 'Activity Log' }),
    ).toBeInTheDocument();
  });

  it('logs out and redirects to /login when the logout menu item is clicked', async () => {
    const user = userEvent.setup();
    renderShell();

    await user.click(screen.getByRole('button', { name: 'Account' }));
    await user.click(screen.getByRole('menuitem', { name: 'Log out' }));

    expect(await screen.findByText('Login screen')).toBeInTheDocument();
  });
});
