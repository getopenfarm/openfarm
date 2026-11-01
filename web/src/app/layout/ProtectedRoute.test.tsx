import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider } from '@/lib/auth/AuthProvider';
import { clearToken, setToken } from '@/lib/auth/tokenStorage';
import { ProtectedRoute } from './ProtectedRoute';

function json(data: unknown, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}
function renderProtected(initialEntry: string) {
  return render(
    <QueryClientProvider
      client={
        new QueryClient({ defaultOptions: { queries: { retry: false } } })
      }
    >
      <AuthProvider>
        <MemoryRouter initialEntries={[initialEntry]}>
          <Routes>
            <Route path="/login" element={<div>Login screen</div>} />
            <Route element={<ProtectedRoute />}>
              <Route path="/" element={<div>Protected content</div>} />
              <Route path="/users-groups" element={<div>Users</div>} />
              <Route path="/settings/fees" element={<div>Fees</div>} />
              <Route path="/profile" element={<div>My Profile</div>} />
            </Route>
          </Routes>
        </MemoryRouter>
      </AuthProvider>
    </QueryClientProvider>,
  );
}
describe('ProtectedRoute', () => {
  afterEach(() => {
    clearToken();
    vi.unstubAllGlobals();
  });
  it('restores the cookie session before deciding whether to redirect', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValueOnce(json({ token: 'csrf' }))
        .mockResolvedValueOnce(json({}, 401)),
    );
    renderProtected('/');
    expect(screen.queryByText('Login screen')).not.toBeInTheDocument();
    expect(await screen.findByText('Login screen')).toBeInTheDocument();
  });
  it('renders authorized content', async () => {
    setToken('valid');
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(json({ permissions: ['printers:list'] })),
    );
    renderProtected('/');
    expect(await screen.findByText('Protected content')).toBeInTheDocument();
  });
  it.each([
    [['printers:list'], 'Protected content'],
    [['settings:fees:read'], 'Fees'],
    [[], 'My Profile'],
  ])(
    'redirects a forbidden page to the first allowed one (%j)',
    async (permissions, landing) => {
      setToken('valid');
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ permissions })));
      renderProtected('/users-groups');
      expect(await screen.findByText(landing)).toBeInTheDocument();
    },
  );
  it('offers retry when session restoration fails offline', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')));
    renderProtected('/');
    expect(
      await screen.findByText('Could not restore your session'),
    ).toBeInTheDocument();
    expect(screen.queryByText('Login screen')).not.toBeInTheDocument();
  });
});
