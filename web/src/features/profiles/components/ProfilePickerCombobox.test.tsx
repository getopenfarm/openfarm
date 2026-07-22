import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ProfilePickerCombobox } from './ProfilePickerCombobox';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function stubFetch(routes: Record<string, unknown>) {
  const fetchMock = vi.fn((input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input.toString();
    for (const [path, body] of Object.entries(routes)) {
      if (url.includes(path)) {
        return Promise.resolve(jsonResponse(body));
      }
    }
    return Promise.reject(new Error(`Unexpected fetch: ${url}`));
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

function renderPicker(
  props: Partial<React.ComponentProps<typeof ProfilePickerCombobox>> = {},
) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );
  }

  const onChange = vi.fn();
  const utils = render(
    <ProfilePickerCombobox
      kind="machine"
      value={null}
      onChange={onChange}
      {...props}
    />,
    { wrapper: Wrapper },
  );
  return { ...utils, onChange };
}

describe('ProfilePickerCombobox', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('renders the placeholder when nothing is selected', () => {
    stubFetch({ '/profiles/machine': { items: [] } });
    renderPicker({ placeholder: 'No machine profile linked' });

    expect(
      screen.getByRole('button', { name: 'No machine profile linked' }),
    ).toBeInTheDocument();
  });

  it('shows the disabled placeholder and stays closed when disabled', async () => {
    const user = userEvent.setup();
    stubFetch({ '/profiles/process': { items: [] } });
    renderPicker({
      kind: 'process',
      disabled: true,
      disabledPlaceholder: 'Select a machine profile first',
    });

    const trigger = screen.getByRole('button', {
      name: 'Select a machine profile first',
    });
    expect(trigger).toBeDisabled();
    await user.click(trigger);
    expect(
      screen.queryByPlaceholderText('Search profiles…'),
    ).not.toBeInTheDocument();
  });

  it('opens on click, lists origin-labeled results, and selects one', async () => {
    const user = userEvent.setup();
    const fetchMock = stubFetch({
      '/profiles/machine': {
        items: [
          {
            id: 'm1',
            name: 'Bambu X1 Carbon 0.4 nozzle',
            origin: 'System',
            vendorName: 'Bambulab',
          },
          { id: 'm2', name: 'My Tuned Printer', origin: 'User' },
        ],
      },
    });
    const { onChange } = renderPicker({
      selectedLabel: null,
      placeholder: 'No machine profile linked',
    });

    await user.click(
      screen.getByRole('button', { name: 'No machine profile linked' }),
    );

    await waitFor(() =>
      expect(
        screen.getByText('Bambu X1 Carbon 0.4 nozzle'),
      ).toBeInTheDocument(),
    );
    expect(screen.getByText('System: Bambulab')).toBeInTheDocument();
    expect(screen.getByText('My Tuned Printer')).toBeInTheDocument();
    expect(screen.getByText('User')).toBeInTheDocument();

    await user.click(screen.getByText('Bambu X1 Carbon 0.4 nozzle'));

    expect(onChange).toHaveBeenCalledWith('m1');
    expect(
      fetchMock.mock.calls.some((call) =>
        call[0].toString().includes('origin=All'),
      ),
    ).toBe(true);
  });

  it('debounces search input before refetching', async () => {
    const user = userEvent.setup();
    const fetchMock = stubFetch({ '/profiles/machine': { items: [] } });
    renderPicker({ placeholder: 'No machine profile linked' });

    await user.click(
      screen.getByRole('button', { name: 'No machine profile linked' }),
    );
    const searchInput = await screen.findByPlaceholderText('Search profiles…');
    await user.type(searchInput, 'X1C');

    await waitFor(
      () =>
        expect(
          fetchMock.mock.calls.some((call) =>
            call[0].toString().includes('q=X1C'),
          ),
        ).toBe(true),
      { timeout: 1000 },
    );
  });
});
