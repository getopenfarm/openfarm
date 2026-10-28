import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider } from '@/lib/auth/AuthProvider';
import { clearToken, setToken } from '@/lib/auth/tokenStorage';
import { PreferencesProvider } from '@/lib/preferences/usePreferences';
import { AppShell } from './AppShell';

function renderShell(initialPath = '/') {
  setToken('a-valid-token');

  const fetchMock = vi.fn((input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input.toString();
    if (url.includes('/hubs/farm')) {
      return Promise.reject(new Error('SignalR not available in tests'));
    }
    const body = url.includes('/auth/whoami')
      ? { username: 'admin', email: 'admin@openfarm.local' }
      : { printers: [] };
    return Promise.resolve(
      new Response(JSON.stringify(body), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
  });
  vi.stubGlobal('fetch', fetchMock);

  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

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
            handle: { title: 'Activity log' },
          },
        ],
      },
    ],
    { initialEntries: [initialPath] },
  );
  return render(
    <QueryClientProvider client={queryClient}>
      <PreferencesProvider>
        <AuthProvider>
          <RouterProvider router={testRouter} />
        </AuthProvider>
      </PreferencesProvider>
    </QueryClientProvider>,
  );
}

describe('AppShell', () => {
  afterEach(() => {
    clearToken();
    vi.unstubAllGlobals();
  });

  it('renders the grouped nav sections', () => {
    renderShell();

    expect(screen.getByRole('link', { name: 'Dashboard' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Printers' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Runs' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Queue' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Filament' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Stats' })).toBeInTheDocument();
    expect(
      screen.queryByRole('link', { name: 'Spools' }),
    ).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Parts' })).toBeInTheDocument();
    expect(
      screen.getByRole('link', { name: 'Assemblies' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Profiles' })).toBeInTheDocument();
    expect(
      screen.getByRole('link', { name: 'Maintenance' }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('link', { name: 'Activity log' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Settings' })).toBeInTheDocument();

    expect(screen.getAllByText('Operate').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Library').length).toBeGreaterThan(0);
  });

  it('shows the current route title in the title bar', () => {
    renderShell('/activity-log');

    expect(
      screen.getByText('Activity log', {
        selector: '[data-slot="breadcrumb-page"]',
      }),
    ).toBeInTheDocument();
  });

  it('logs out and redirects to /login when the logout menu item is clicked', async () => {
    const user = userEvent.setup();
    renderShell();

    await user.click(await screen.findByRole('button', { name: /admin/ }));
    expect(screen.getAllByText('admin@openfarm.local').length).toBeGreaterThan(
      0,
    );
    await user.click(screen.getByRole('menuitem', { name: 'Log out' }));

    expect(await screen.findByText('Login screen')).toBeInTheDocument();
  });

  it('collapses and expands the sidebar from the bottom button', async () => {
    const user = userEvent.setup();
    renderShell();
    const sidebar = () =>
      document.querySelector('[data-slot="sidebar"][data-state]');

    expect(sidebar()).toHaveAttribute('data-state', 'expanded');
    await user.click(screen.getByRole('button', { name: 'Collapse' }));
    expect(sidebar()).toHaveAttribute('data-state', 'collapsed');
    await user.click(screen.getByRole('button', { name: 'Collapse' }));
    expect(sidebar()).toHaveAttribute('data-state', 'expanded');
  });
});
