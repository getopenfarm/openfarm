import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { toast } from 'sonner';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { httpClient } from '@/lib/api/httpClient';
import { SignInMethods } from './SignInMethods';

vi.mock('@/lib/api/httpClient', () => ({ httpClient: vi.fn() }));
vi.mock('sonner', () => ({ toast: { success: vi.fn() } }));
const http = vi.mocked(httpClient);

interface Identity {
  enabled: boolean;
  displayName: string;
  linked: boolean;
  passwordAllowed: boolean;
}

const notLinked: Identity = {
  enabled: true,
  displayName: 'authentik',
  linked: false,
  passwordAllowed: true,
};

/** Answers the identity GET, the CSRF GET, and each link POST in turn. */
function serve(identity: Identity, ...links: unknown[]) {
  http.mockImplementation(async (url) => {
    if (url === '/api/v1/auth/sso/identity') return { data: identity };
    if (url === '/api/v1/auth/csrf') return { data: { token: 'csrf-proof' } };
    const next = links.shift();
    if (next instanceof Response) throw next;
    return { data: next };
  });
}

const linkPosts = () =>
  http.mock.calls.filter(([url]) => url === '/api/v1/auth/sso/link');

function setup(search = '') {
  return render(
    <QueryClientProvider
      client={
        new QueryClient({ defaultOptions: { queries: { retry: false } } })
      }
    >
      <MemoryRouter initialEntries={[`/profile?tab=security${search}`]}>
        <SignInMethods
          passwordForm={(close) => (
            <button type="button" onClick={close}>
              Password form
            </button>
          )}
        />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

const assign = vi.fn();
beforeEach(() => {
  http.mockReset();
  assign.mockReset();
  vi.stubGlobal('location', { ...window.location, assign });
});
afterEach(() => vi.unstubAllGlobals());

describe('Sign-in methods', () => {
  it('links straight away after a recent sign-in, with no password field', async () => {
    serve(notLinked, {
      redirectUri: '/api/v1/auth/sso/link',
      passwordRequired: false,
    });
    setup();
    expect(
      await screen.findByText('Not linked.', { exact: false }),
    ).toBeInTheDocument();
    expect(screen.queryByLabelText('Current password')).not.toBeInTheDocument();
    await userEvent
      .setup()
      .click(screen.getByRole('button', { name: 'Link authentik' }));
    await waitFor(() => expect(assign).toHaveBeenCalledTimes(1));
    expect(assign.mock.calls[0][0]).toContain('/api/v1/auth/sso/link');
    expect(linkPosts()).toEqual([
      [
        '/api/v1/auth/sso/link',
        {
          method: 'POST',
          headers: { 'X-CSRF-TOKEN': 'csrf-proof' },
          body: '{}',
        },
      ],
    ]);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('asks for the password when the sign-in is older, and keeps the dialog open on a wrong one', async () => {
    serve(
      notLinked,
      { redirectUri: null, passwordRequired: true },
      new Response(JSON.stringify({ detail: 'Check the current password.' }), {
        status: 400,
        headers: { 'Content-Type': 'application/json' },
      }),
      { redirectUri: '/api/v1/auth/sso/link', passwordRequired: false },
    );
    setup();
    const user = userEvent.setup();
    await user.click(
      await screen.findByRole('button', { name: 'Link authentik' }),
    );
    const dialog = await screen.findByRole('dialog', {
      name: 'Confirm your password',
    });
    const password = within(dialog).getByLabelText('Current password');
    expect(password).toHaveAttribute('autocomplete', 'current-password');

    await user.type(password, 'incorrect');
    await user.click(within(dialog).getByRole('button', { name: 'Continue' }));
    expect(
      await within(dialog).findByText('Check the current password.'),
    ).toBeInTheDocument();
    expect(password).toHaveAccessibleDescription('Check the current password.');
    expect(assign).not.toHaveBeenCalled();

    await user.type(password, 'admin-openfarm');
    await user.click(within(dialog).getByRole('button', { name: 'Continue' }));
    await waitFor(() => expect(assign).toHaveBeenCalledTimes(1));
    expect(linkPosts().at(-1)?.[1]).toMatchObject({
      body: JSON.stringify({ password: 'admin-openfarm' }),
    });
  });

  it('shows a linked provider as a badge with no link action', async () => {
    serve({ ...notLinked, linked: true });
    setup();
    expect(await screen.findByText('Linked')).toBeInTheDocument();
    expect(screen.getByText('Sign in with authentik')).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Link authentik' }),
    ).not.toBeInTheDocument();
  });

  it('announces a completed link once', async () => {
    serve({ ...notLinked, linked: true });
    setup('&sso=linked');
    await waitFor(() =>
      expect(toast.success).toHaveBeenCalledWith('authentik linked.', {
        id: 'sso-linked',
      }),
    );
  });

  it('opens the password form from the Password row and closes it again', async () => {
    serve(notLinked);
    setup();
    const user = userEvent.setup();
    expect(screen.queryByText('Password form')).not.toBeInTheDocument();
    await user.click(await screen.findByRole('button', { name: 'Change' }));
    expect(screen.getByRole('button', { name: 'Change' })).toHaveAttribute(
      'aria-expanded',
      'true',
    );
    await user.click(screen.getByText('Password form'));
    expect(screen.queryByText('Password form')).not.toBeInTheDocument();
  });

  it('marks password sign-in as turned off when SSO is required', async () => {
    serve({ ...notLinked, linked: true, passwordAllowed: false });
    setup();
    expect(
      await screen.findByText('Turned off. Sign in with authentik.'),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Change' }),
    ).not.toBeInTheDocument();
  });

  it('shows only the password row when SSO is off', async () => {
    serve({ ...notLinked, enabled: false });
    setup();
    expect(
      await screen.findByText('Username and password'),
    ).toBeInTheDocument();
    expect(screen.queryByText('authentik')).not.toBeInTheDocument();
  });

  it('explains a failed link from the provider', async () => {
    serve(notLinked);
    setup('&sso=failed&ssoReason=provider_configuration');
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Check the issuer URL',
    );
    expect(screen.getByRole('alert')).toHaveTextContent('see the server log');
    expect(
      screen.getByRole('button', { name: 'Link authentik' }),
    ).toBeEnabled();
  });

  it('uses the neutral label the server sends for an unnamed provider', async () => {
    serve({ ...notLinked, displayName: 'SSO' });
    setup();
    expect(
      await screen.findByRole('button', { name: 'Link SSO' }),
    ).toBeEnabled();
  });
});
