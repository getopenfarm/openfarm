import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { httpClient } from '@/lib/api/httpClient';
import { OidcSecurity } from './OidcSecurity';

vi.mock('@/lib/api/httpClient', () => ({ httpClient: vi.fn() }));
const http = vi.mocked(httpClient);

function setup(search = '') {
  return render(
    <QueryClientProvider
      client={
        new QueryClient({ defaultOptions: { queries: { retry: false } } })
      }
    >
      <MemoryRouter initialEntries={[`/profile?tab=security${search}`]}>
        <OidcSecurity>
          <div>Local password controls</div>
        </OidcSecurity>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

beforeEach(() => http.mockReset());
afterEach(() => vi.unstubAllGlobals());

describe('OIDC account security', () => {
  it('shows linked status without offering another link or disabled password controls', async () => {
    http.mockResolvedValue({
      data: {
        enabled: true,
        displayName: 'authentik',
        linked: true,
        passwordAllowed: false,
      },
    });
    setup('&sso=linked');
    expect(await screen.findByText('Linked to authentik.')).toBeInTheDocument();
    expect(screen.getByText('Sign-in provider linked.')).toBeInTheDocument();
    expect(
      screen.queryByLabelText('Confirm local password'),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByText('Local password controls'),
    ).not.toBeInTheDocument();
  });

  it('keeps local password controls when OIDC is disabled', async () => {
    http.mockResolvedValue({
      data: {
        enabled: false,
        displayName: 'authentik',
        linked: false,
        passwordAllowed: true,
      },
    });
    setup();
    expect(
      await screen.findByText('Local password controls'),
    ).toBeInTheDocument();
    expect(screen.queryByText('Single sign-on')).not.toBeInTheDocument();
  });

  it('submits password proof with CSRF and keeps the form available after rejection', async () => {
    http.mockResolvedValueOnce({
      data: {
        enabled: true,
        displayName: 'authentik',
        linked: false,
        passwordAllowed: true,
      },
    });
    http.mockResolvedValueOnce({ data: { token: 'csrf-proof' } });
    http.mockRejectedValueOnce(
      new Response(JSON.stringify({ detail: 'Check your current password.' }), {
        status: 400,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    setup();
    const password = await screen.findByLabelText('Confirm local password');
    const user = userEvent.setup();
    await user.type(password, 'incorrect-password');
    await user.click(screen.getByRole('button', { name: 'Link authentik' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Check your current password.',
    );
    expect(http).toHaveBeenLastCalledWith('/api/v1/auth/sso/link', {
      method: 'POST',
      headers: { 'X-CSRF-TOKEN': 'csrf-proof' },
      body: JSON.stringify({ password: 'incorrect-password' }),
    });
    expect(http).toHaveBeenCalledWith('/api/v1/auth/csrf', {
      cache: 'no-store',
    });
    expect(
      screen.getByRole('button', { name: 'Link authentik' }),
    ).toBeEnabled();
    expect(password).toHaveAttribute('autocomplete', 'current-password');
  });

  it('distinguishes provider configuration failures from rejected identity links', async () => {
    http.mockResolvedValue({
      data: {
        enabled: true,
        displayName: 'authentik',
        linked: false,
        passwordAllowed: true,
      },
    });
    setup('&sso=failed&ssoReason=provider_configuration');
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Check the Issuer URL',
    );
    expect(screen.getByRole('alert')).toHaveTextContent('OpenFarm server logs');
    expect(
      screen.getByRole('button', { name: 'Link authentik' }),
    ).toBeEnabled();
    expect(screen.getByText('Local password controls')).toBeInTheDocument();
  });

  it.each([
    ['fresh_authentication', 'The provider did not confirm a recent sign-in.'],
    [
      'browser_proof',
      'The linking browser proof is missing or does not match.',
    ],
    ['session_changed', 'Your original OpenFarm session changed or expired.'],
    ['expired', 'The linking request expired or could not be read.'],
    ['request_replaced', 'The linking request was already used or replaced.'],
    ['configuration_changed', 'The sign-in settings changed during linking.'],
    [
      'already_linked',
      'This OpenFarm account is already linked to the provider.',
    ],
    [
      'identity_conflict',
      'This provider identity is already linked to an OpenFarm account.',
    ],
    [
      'provider_response',
      'The provider sign-in response could not be verified.',
    ],
    ['unknown', 'Could not link this identity.'],
    ['__proto__', 'Could not link this identity.'],
    ['toString', 'Could not link this identity.'],
  ])(
    'explains %s without removing retry or local password controls',
    async (reason, message) => {
      http.mockResolvedValue({
        data: {
          enabled: true,
          displayName: 'authentik',
          linked: false,
          passwordAllowed: true,
        },
      });
      setup(`&sso=failed&ssoReason=${reason}`);
      expect(await screen.findByRole('alert')).toHaveTextContent(message);
      expect(
        screen.getByRole('button', { name: 'Link authentik' }),
      ).toBeEnabled();
      expect(screen.getByText('Local password controls')).toBeInTheDocument();
    },
  );

  it('reports a failed provider lookup and allows retry', async () => {
    http.mockRejectedValueOnce(new Error('Unavailable'));
    http.mockResolvedValueOnce({
      data: {
        enabled: true,
        displayName: 'authentik',
        linked: true,
        passwordAllowed: true,
      },
    });
    setup();
    expect(
      await screen.findByText('Could not load sign-in methods'),
    ).toBeInTheDocument();
    await userEvent
      .setup()
      .click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByText('Linked to authentik.')).toBeInTheDocument();
  });
});
