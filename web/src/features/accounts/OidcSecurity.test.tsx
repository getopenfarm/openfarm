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
    expect(screen.getByText('Account linked.')).toBeInTheDocument();
    expect(screen.queryByLabelText('Current password')).not.toBeInTheDocument();
    expect(
      screen.queryByText('Local password controls'),
    ).not.toBeInTheDocument();
  });

  it('uses the neutral label the server sends for an unnamed provider', async () => {
    http.mockResolvedValue({
      data: {
        enabled: true,
        displayName: 'SSO',
        linked: false,
        passwordAllowed: true,
      },
    });
    setup();
    expect(
      await screen.findByRole('button', { name: 'Link SSO' }),
    ).toBeEnabled();
    expect(
      screen.getByText('Link this account to SSO. Permissions do not change.'),
    ).toBeInTheDocument();
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
      new Response(JSON.stringify({ detail: 'Check the current password.' }), {
        status: 400,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    setup();
    const password = await screen.findByLabelText('Current password');
    const user = userEvent.setup();
    await user.type(password, 'incorrect-password');
    await user.click(screen.getByRole('button', { name: 'Link authentik' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Check the current password.',
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
      'Check the issuer URL',
    );
    expect(screen.getByRole('alert')).toHaveTextContent('see the server log');
    expect(
      screen.getByRole('button', { name: 'Link authentik' }),
    ).toBeEnabled();
    expect(screen.getByText('Local password controls')).toBeInTheDocument();
  });

  it.each([
    ['fresh_authentication', 'The provider did not confirm a recent sign-in.'],
    ['auth_time_missing', 'The provider did not send auth_time.'],
    ['provider_denied', 'The provider refused the sign-in.'],
    ['issuer_mismatch', 'The provider issuer does not match'],
    [
      'browser_proof',
      'The browser proof for linking is missing or does not match.',
    ],
    ['session_changed', 'Your OpenFarm session changed or ended.'],
    ['expired', 'The linking request expired.'],
    ['request_replaced', 'The linking request was used or replaced.'],
    ['configuration_changed', 'The sign-in settings changed.'],
    ['already_linked', 'This account is already linked to the provider.'],
    [
      'identity_conflict',
      'Another OpenFarm account already uses this provider identity.',
    ],
    ['provider_response', 'The provider response failed verification.'],
    ['unknown', 'Link failed.'],
    ['__proto__', 'Link failed.'],
    ['toString', 'Link failed.'],
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
    expect(await screen.findByText('Load failed')).toBeInTheDocument();
    await userEvent
      .setup()
      .click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByText('Linked to authentik.')).toBeInTheDocument();
  });
});
