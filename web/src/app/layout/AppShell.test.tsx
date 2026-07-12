import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router';
import { afterEach, describe, expect, it } from 'vitest';
import { AuthProvider } from '@/lib/auth/AuthProvider';
import { clearToken, setToken } from '@/lib/auth/tokenStorage';
import { AppShell } from './AppShell';

function renderShell() {
  setToken('a-valid-token');
  return render(
    <AuthProvider>
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route path="/login" element={<div>Login screen</div>} />
          <Route element={<AppShell />}>
            <Route path="/" element={<div>Dashboard content</div>} />
          </Route>
        </Routes>
      </MemoryRouter>
    </AuthProvider>,
  );
}

describe('AppShell', () => {
  afterEach(() => {
    clearToken();
  });

  it('renders the nav items', () => {
    renderShell();

    expect(screen.getByRole('link', { name: 'Dashboard' })).toBeInTheDocument();
    expect(
      screen.getByRole('link', { name: 'Connection Secrets' }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('link', { name: 'Activity Log' }),
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
