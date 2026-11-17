import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { refreshSession } from '@/lib/auth/session';
import { OidcCompletePage } from './OidcCompletePage';

const { login } = vi.hoisted(() => ({ login: vi.fn() }));
vi.mock('@/lib/auth/useAuth', () => ({ useAuth: () => ({ login }) }));
vi.mock('@/lib/auth/session', () => ({ refreshSession: vi.fn() }));

function Destination() {
  return (
    <div>
      Destination: {useLocation().pathname}
      {useLocation().search}
    </div>
  );
}
function setup(returnTo: string) {
  render(
    <MemoryRouter
      initialEntries={[
        `/auth/complete?returnTo=${encodeURIComponent(returnTo)}`,
      ]}
    >
      <Routes>
        <Route path="/auth/complete" element={<OidcCompletePage />} />
        <Route path="*" element={<Destination />} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.mocked(refreshSession).mockReset();
  login.mockReset();
});

describe('OIDC completion', () => {
  it('restores the cookie session before returning to MCP consent', async () => {
    vi.mocked(refreshSession).mockResolvedValue('openfarm-token');
    setup('/connect?request=example');
    expect(
      await screen.findByText('Destination: /connect?request=example'),
    ).toBeInTheDocument();
    expect(login).toHaveBeenCalledExactlyOnceWith('openfarm-token');
  });
  it('rejects external return destinations', async () => {
    vi.mocked(refreshSession).mockResolvedValue('openfarm-token');
    setup('//attacker.example');
    expect(await screen.findByText('Destination: /')).toBeInTheDocument();
  });
  it('does not report success when the refresh cookie is missing', async () => {
    vi.mocked(refreshSession).mockResolvedValue(null);
    setup('/printers');
    expect(
      await screen.findByText('Could not finish sign-in'),
    ).toBeInTheDocument();
    expect(login).not.toHaveBeenCalled();
  });
  it('offers recovery after a network failure', async () => {
    vi.mocked(refreshSession).mockRejectedValue(new Error('Offline'));
    setup('/printers');
    await waitFor(() =>
      expect(
        screen.getByRole('link', { name: 'Return to sign in' }),
      ).toHaveAttribute('href', '/login'),
    );
    expect(login).not.toHaveBeenCalled();
  });
});
