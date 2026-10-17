import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { VendorCombobox, type VendorValue } from './VendorCombobox';

const fetchMock = vi.fn((_input: RequestInfo | URL, _init?: RequestInit) =>
  Promise.resolve(
    new Response(
      JSON.stringify({
        vendors: [
          { id: 'v-2', name: 'Prusament' },
          { id: 'v-1', name: 'Polymaker' },
        ],
      }),
      { status: 200, headers: { 'Content-Type': 'application/json' } },
    ),
  ),
);

function renderCombobox(initial: VendorValue = { vendorId: '' }) {
  const onChange = vi.fn();
  function Harness() {
    const [value, setValue] = useState(initial);
    return (
      <VendorCombobox
        aria-label="Vendor"
        value={value}
        onChange={(next) => {
          onChange(next);
          setValue(next);
        }}
      />
    );
  }
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  render(
    <QueryClientProvider client={queryClient}>
      <Harness />
    </QueryClientProvider>,
  );
  return { onChange, input: screen.getByRole('combobox', { name: 'Vendor' }) };
}

describe('VendorCombobox', () => {
  beforeEach(() => vi.stubGlobal('fetch', fetchMock));
  afterEach(() => {
    vi.unstubAllGlobals();
    fetchMock.mockClear();
  });

  it('lists vendors alphabetically on focus', async () => {
    const user = userEvent.setup();
    const { input } = renderCombobox();

    await user.click(input);

    const options = await screen.findAllByRole('option');
    expect(options.map((o) => o.textContent)).toEqual([
      'Polymaker',
      'Prusament',
    ]);
  });

  it('stages an unknown name as a new vendor without creating it', async () => {
    const user = userEvent.setup();
    const { input, onChange } = renderCombobox();

    await user.type(input, '  Sunlu ');
    const options = await screen.findAllByRole('option');
    expect(options[0]).toHaveTextContent('Add "Sunlu"');
    await user.keyboard('{Enter}');

    expect(onChange).toHaveBeenLastCalledWith({
      vendorId: '',
      newVendorName: 'Sunlu',
    });
    expect(input).toHaveValue('Sunlu');
    expect(screen.getByText('New')).toBeInTheDocument();
    expect(
      fetchMock.mock.calls.some(([, init]) => init?.method === 'POST'),
    ).toBe(false);
  });

  it('puts Add after partial matches so Enter picks the match', async () => {
    const user = userEvent.setup();
    const { input, onChange } = renderCombobox();

    await user.type(input, 'pru');
    const listbox = await screen.findByRole('listbox');
    const options = within(listbox).getAllByRole('option');
    expect(options.map((o) => o.textContent)).toEqual([
      'Prusament',
      'Add "pru"',
    ]);
    await user.keyboard('{Enter}');

    expect(onChange).toHaveBeenLastCalledWith({ vendorId: 'v-2' });
  });

  it('hides Add for a case-insensitive exact match', async () => {
    const user = userEvent.setup();
    const { input } = renderCombobox();

    await user.type(input, 'prusament');

    expect(
      await screen.findByRole('option', { name: 'Prusament' }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('option', { name: /Add "/ }),
    ).not.toBeInTheDocument();
  });

  it('clears the selection', async () => {
    const user = userEvent.setup();
    const { input, onChange } = renderCombobox({ vendorId: 'v-2' });
    expect(await screen.findByDisplayValue('Prusament')).toBe(input);

    await user.click(screen.getByRole('button', { name: 'Clear vendor' }));

    expect(onChange).toHaveBeenLastCalledWith({ vendorId: '' });
  });
});
