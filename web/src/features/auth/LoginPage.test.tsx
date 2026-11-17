import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { AuthProvider } from '@/lib/auth/AuthProvider';
import { useLoginProviders } from '@/lib/auth/oidc';
import { clearToken, getToken } from '@/lib/auth/tokenStorage';
import { LoginPage } from './LoginPage';

vi.mock('@/lib/auth/oidc', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/auth/oidc')>()),
  useLoginProviders: vi.fn(),
}));

vi.mock('@/lib/auth/session', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/auth/session')>()),
  refreshSession: vi.fn().mockResolvedValue(null),
}));

function renderLoginPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        <AuthProvider>
          <MemoryRouter initialEntries={['/login']}>
            {children}
            <Toaster />
          </MemoryRouter>
        </AuthProvider>
      </QueryClientProvider>
    );
  }

  return render(<LoginPage />, { wrapper: Wrapper });
}

describe('LoginPage', () => {
  beforeEach(() => {
    clearToken();
    vi.mocked(useLoginProviders).mockReturnValue({
      data: {
        oidcEnabled: false,
        oidcRequired: false,
        displayName: 'authentik',
      },
      isError: false,
    } as ReturnType<typeof useLoginProviders>);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    clearToken();
  });

  it('renders the username, password fields and submit button', () => {
    renderLoginPage();

    expect(screen.getByLabelText('Username')).toBeInTheDocument();
    expect(screen.getByLabelText('Password')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument();
  });

  it('offers OIDC alongside local login in optional mode', () => {
    vi.mocked(useLoginProviders).mockReturnValue({
      data: {
        oidcEnabled: true,
        oidcRequired: false,
        displayName: 'authentik',
      },
      isError: false,
    } as ReturnType<typeof useLoginProviders>);
    renderLoginPage();
    expect(
      screen.getByRole('link', { name: 'Sign in with authentik' }),
    ).toHaveAttribute('href', '/api/v1/auth/sso/login?returnTo=%2F');
    expect(screen.getByLabelText('Password')).toBeInTheDocument();
  });

  it('reserves the password form for recovery when OIDC is required', async () => {
    vi.mocked(useLoginProviders).mockReturnValue({
      data: { oidcEnabled: true, oidcRequired: true, displayName: 'authentik' },
      isError: false,
    } as ReturnType<typeof useLoginProviders>);
    renderLoginPage();
    expect(screen.queryByLabelText('Password')).not.toBeInTheDocument();
    await userEvent
      .setup()
      .click(screen.getByRole('button', { name: 'Use recovery account' }));
    expect(screen.getByLabelText('Password')).toBeInTheDocument();
  });

  it('blocks submission and shows inline errors when fields are empty', async () => {
    const fetchSpy = vi.fn();
    vi.stubGlobal('fetch', fetchSpy);
    const user = userEvent.setup();

    renderLoginPage();
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findAllByText('Username is required')).not.toHaveLength(
      0,
    );
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it('stores the token and does not error on a successful login', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response(
          JSON.stringify({
            token: 'test-token',
            expiresAtUtc: '2026-01-01T00:00:00Z',
          }),
          {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          },
        ),
      ),
    );
    const user = userEvent.setup();

    renderLoginPage();
    await user.type(screen.getByLabelText('Username'), 'admin');
    await user.type(screen.getByLabelText('Password'), 'admin-openfarm');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    await waitFor(() => expect(getToken()).toBe('test-token'));
  });

  it('shows an inline error and does not store a token on invalid credentials', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response(null, { status: 401 })),
    );
    const user = userEvent.setup();

    renderLoginPage();
    await user.type(screen.getByLabelText('Username'), 'admin');
    await user.type(screen.getByLabelText('Password'), 'wrong-password');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    await waitFor(() =>
      expect(
        screen.getByText('Check username and password.'),
      ).toBeInTheDocument(),
    );
    expect(getToken()).toBeNull();
  });
});
