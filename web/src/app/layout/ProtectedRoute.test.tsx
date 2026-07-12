import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router';
import { afterEach, describe, expect, it } from 'vitest';
import { AuthProvider } from '@/lib/auth/AuthProvider';
import { clearToken, setToken } from '@/lib/auth/tokenStorage';
import { ProtectedRoute } from './ProtectedRoute';

function renderProtected(initialEntry: string) {
  return render(
    <AuthProvider>
      <MemoryRouter initialEntries={[initialEntry]}>
        <Routes>
          <Route path="/login" element={<div>Login screen</div>} />
          <Route element={<ProtectedRoute />}>
            <Route path="/" element={<div>Protected content</div>} />
          </Route>
        </Routes>
      </MemoryRouter>
    </AuthProvider>,
  );
}

describe('ProtectedRoute', () => {
  afterEach(() => {
    clearToken();
  });

  it('redirects to /login when there is no token', () => {
    renderProtected('/');

    expect(screen.getByText('Login screen')).toBeInTheDocument();
    expect(screen.queryByText('Protected content')).not.toBeInTheDocument();
  });

  it('renders the protected content when a token is present', () => {
    setToken('a-valid-token');

    renderProtected('/');

    expect(screen.getByText('Protected content')).toBeInTheDocument();
  });
});
