import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { describe, expect, it } from 'vitest';
import { ConnectAssistantCard } from './ConnectAssistantCard';
import { assistantClients } from './connectSnippets';

function setup(props: Partial<Parameters<typeof ConnectAssistantCard>[0]>) {
  return render(
    <MemoryRouter>
      <ConnectAssistantCard
        mcpUrl="https://farm.example.com/mcp"
        signInEnabled={false}
        mcpEnabled
        defaultOpen
        {...props}
      />
    </MemoryRouter>,
  );
}

describe('ConnectAssistantCard', () => {
  it('shows the server URL and token setup when sign-in is off', async () => {
    setup({});
    const user = userEvent.setup();
    expect(
      screen.getByText('https://farm.example.com/mcp'),
    ).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent(
      'Sign-in is off. Use an access token, or ask an administrator to enable sign-in under Settings → AI assistants.',
    );
    expect(
      screen.getByRole('link', { name: 'How to turn on sign-in' }),
    ).toHaveAttribute(
      'href',
      'https://getopenfarm.github.io/docs/administration/ai-assistants#prepare-the-server',
    );
    expect(
      screen.queryByRole('heading', { name: 'Sign in' }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByText(/--header "Authorization: Bearer \$OPENFARM_TOKEN"/),
    ).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Copy server URL' }));
    expect(await navigator.clipboard.readText()).toBe(
      'https://farm.example.com/mcp',
    );
    await user.click(screen.getByRole('tab', { name: 'Claude' }));
    expect(screen.getByText('Needs sign-in.')).toBeInTheDocument();
  });

  it('shows the disabled state without connection snippets', () => {
    setup({ mcpEnabled: false });
    expect(screen.getByRole('status')).toHaveTextContent(
      'MCP is disabled for this installation.',
    );
    expect(
      screen.queryByRole('button', { name: 'Copy server URL' }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('link', { name: 'Add token' }),
    ).not.toBeInTheDocument();
  });

  it('shows both ways when sign-in is on', () => {
    setup({ signInEnabled: true });
    expect(
      screen.getByRole('heading', { name: 'Sign in' }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: 'With an access token' }),
    ).toBeInTheDocument();
  });

  it('warns when the address is plain HTTP on another host', () => {
    setup({ mcpUrl: 'http://farm.lan:6868/mcp' });
    expect(screen.getByText(/This address is not HTTPS/)).toBeInTheDocument();
  });

  it('never puts a real credential in a snippet', () => {
    const all = JSON.stringify(
      assistantClients('https://farm.example.com/mcp'),
    );
    expect(all).not.toMatch(/ofp_[0-9a-f]{8}/);
  });
});
