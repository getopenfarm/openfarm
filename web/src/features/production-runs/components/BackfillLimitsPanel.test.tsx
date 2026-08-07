import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { BackfillLimitsPanel } from './BackfillLimitsPanel';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function stubFetch() {
  const fetchMock = vi.fn(() =>
    Promise.resolve(
      jsonResponse({
        id: 'line-1',
        backfillPrinterCap: 2,
        backfillCumulativeUnitCeiling: null,
      }),
    ),
  );
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

function renderPanel(
  props: Partial<React.ComponentProps<typeof BackfillLimitsPanel>> = {},
) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );
  }

  return render(
    <BackfillLimitsPanel
      runId="run-1"
      lineId="line-1"
      backfillPrinterCap={1}
      backfillCumulativeUnitCeiling={null}
      cumulativeProducedUnits={12}
      {...props}
    />,
    { wrapper: Wrapper },
  );
}

describe('BackfillLimitsPanel', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('renders the cumulative produced count only, never a percentage', () => {
    stubFetch();
    renderPanel({ cumulativeProducedUnits: 12 });

    expect(screen.getByText('12 produced so far')).toBeInTheDocument();
    expect(screen.queryByText(/%/)).not.toBeInTheDocument();
  });

  it('rejects a printer cap below 1', async () => {
    const fetchMock = stubFetch();
    const user = userEvent.setup();
    renderPanel();

    const capInput = screen.getByLabelText('Printer cap');
    await user.clear(capInput);
    await user.type(capInput, '0');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('rejects a negative cumulative ceiling', async () => {
    const fetchMock = stubFetch();
    const user = userEvent.setup();
    renderPanel();

    const ceilingInput = screen.getByLabelText('Cumulative ceiling');
    await user.clear(ceilingInput);
    await user.type(ceilingInput, '-5');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('submits valid limits', async () => {
    const fetchMock = stubFetch();
    const user = userEvent.setup();
    renderPanel();

    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(fetchMock).toHaveBeenCalled();
  });
});
