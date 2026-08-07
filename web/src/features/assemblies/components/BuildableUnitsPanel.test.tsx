import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { BuildableUnitsPanel } from './BuildableUnitsPanel';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function renderPanel() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );
  }

  return render(<BuildableUnitsPanel assemblyId="a1" />, { wrapper: Wrapper });
}

describe('BuildableUnitsPanel', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('renders a not-applicable result distinctly from a value of 0', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString();
        if (url.includes('/explosion')) {
          return Promise.resolve(
            jsonResponse({
              printedParts: [
                { partId: 'p1', partName: 'Bracket', quantity: 2 },
              ],
              nonPrintedComponents: [],
            }),
          );
        }
        if (url.includes('/buildable-units')) {
          return Promise.resolve(
            jsonResponse({
              isApplicable: false,
              value: null,
              notApplicableReason: 'Assembly contains no printed parts.',
            }),
          );
        }
        return Promise.reject(new Error(`Unexpected fetch: ${url}`));
      }),
    );

    renderPanel();

    const input = await screen.findByLabelText('Bracket on hand');
    const user = userEvent.setup();
    await user.type(input, '0');

    expect(await screen.findByText(/Not applicable/)).toBeInTheDocument();
    expect(
      screen.getByText(/Assembly contains no printed parts\./),
    ).toBeInTheDocument();
  });

  it('renders an applicable result of 0 as a real value, not "not applicable"', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString();
        if (url.includes('/explosion')) {
          return Promise.resolve(
            jsonResponse({
              printedParts: [
                { partId: 'p1', partName: 'Bracket', quantity: 2 },
              ],
              nonPrintedComponents: [],
            }),
          );
        }
        if (url.includes('/buildable-units')) {
          return Promise.resolve(
            jsonResponse({
              isApplicable: true,
              value: 0,
              notApplicableReason: null,
            }),
          );
        }
        return Promise.reject(new Error(`Unexpected fetch: ${url}`));
      }),
    );

    renderPanel();

    const input = await screen.findByLabelText('Bracket on hand');
    const user = userEvent.setup();
    await user.type(input, '0');

    expect(await screen.findByText('Buildable units:')).toBeInTheDocument();
    expect(screen.queryByText(/Not applicable/)).not.toBeInTheDocument();
  });
});
