import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  type DisplayFilamentRequirement,
  PartFilamentRequirementsList,
} from './PartFilamentRequirementsList';

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
    return Promise.resolve(jsonResponse({}));
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

function renderList(
  requirements: DisplayFilamentRequirement[],
  onNarrowToProfile: (
    requirementId: string,
    filamentProfileId: string | null,
  ) => void,
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
    <PartFilamentRequirementsList
      requirements={requirements}
      onNarrowToProfile={onNarrowToProfile}
    />,
    { wrapper: Wrapper },
  );
}

describe('PartFilamentRequirementsList (US4 narrowing control)', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('does not replace an unset catalog choice with a generic profile picker', async () => {
    stubFetch({ '/filaments': { filaments: [] } });
    renderList(
      [
        {
          key: 'req-1',
          materialType: 'PLA',
          detectedColorHex: '#ffffff',
          detectedSubBrand: null,
        },
      ],
      vi.fn(),
    );
    expect(
      screen.queryByRole('button', { name: 'Match by generic type only' }),
    ).not.toBeInTheDocument();
  });

  it('shows the persisted catalog variant and filters incompatible materials', async () => {
    const user = userEvent.setup();
    stubFetch({
      '/filaments': {
        filaments: [
          {
            id: 'filament-1',
            vendorName: 'Acme',
            material: 'PETG Basic',
            materialType: 'PETG',
            variants: [
              { id: 'variant-1', colorName: 'Black', colorHex: '#27272a' },
            ],
          },
          {
            id: 'filament-2',
            vendorName: 'Acme',
            material: 'PLA Basic',
            materialType: 'PLA',
            variants: [
              { id: 'variant-2', colorName: 'White', colorHex: '#ffffff' },
            ],
          },
        ],
      },
    });
    const onNarrow = vi.fn();
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    render(
      <QueryClientProvider client={queryClient}>
        <PartFilamentRequirementsList
          requirements={[
            {
              key: 'req-1',
              materialType: 'PETG',
              detectedColorHex: '#000000',
              detectedSubBrand: null,
              narrowFilamentVariantId: 'variant-1',
            },
          ]}
          onNarrow={onNarrow}
        />
      </QueryClientProvider>,
    );
    const trigger = await screen.findByRole('button', {
      name: /Production filament for PETG: Acme PETG Basic · Black/,
    });
    await user.click(trigger);
    // Done without a change keeps the caller's value, which may hold a profile.
    await user.click(screen.getByRole('button', { name: 'Done' }));
    expect(onNarrow).not.toHaveBeenCalled();
    await user.click(trigger);
    expect(screen.queryByText(/PLA Basic/)).not.toBeInTheDocument();
    // The chosen colour's filament opens with it checked.
    expect(
      screen.getByRole('radio', { name: 'Acme PETG Basic · Black' }),
    ).toBeChecked();
    await user.click(screen.getByRole('radio', { name: 'Any PETG · #000000' }));
    await user.click(screen.getByRole('button', { name: 'Done' }));
    expect(onNarrow).toHaveBeenCalledWith('req-1', {
      variantId: null,
      filamentId: null,
      anyColor: false,
    });
  });

  it('chooses any color of a filament, and Cancel keeps the choice', async () => {
    const user = userEvent.setup();
    stubFetch({
      '/filaments': {
        filaments: [
          {
            id: 'filament-1',
            vendorName: 'Aliz',
            material: 'PETG Basic',
            materialType: 'PETG',
            variants: [
              { id: 'variant-1', colorName: 'Black', colorHex: '#000000' },
              { id: 'variant-2', colorName: 'Grey', colorHex: '#888888' },
            ],
          },
        ],
      },
    });
    const onNarrow = vi.fn();
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    render(
      <QueryClientProvider client={queryClient}>
        <PartFilamentRequirementsList
          requirements={[
            {
              key: 'req-1',
              materialType: 'PETG',
              detectedColorHex: '#000000',
              detectedSubBrand: null,
              narrowFilamentId: 'filament-1',
            },
          ]}
          onNarrow={onNarrow}
        />
      </QueryClientProvider>,
    );
    const trigger = await screen.findByRole('button', {
      name: /Aliz PETG Basic · Any color/,
    });
    await user.click(trigger);
    expect(
      screen.getByRole('radio', { name: 'Aliz PETG Basic · Any color' }),
    ).toBeChecked();
    await user.click(screen.getByRole('radio', { name: 'Any PETG · #000000' }));
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onNarrow).not.toHaveBeenCalled();

    await user.click(trigger);
    await user.type(
      screen.getByRole('searchbox', { name: 'Search filament or color' }),
      'grey',
    );
    expect(
      screen.queryByRole('radio', { name: 'Aliz PETG Basic · Black' }),
    ).not.toBeInTheDocument();
    await user.click(
      screen.getByRole('radio', { name: 'Aliz PETG Basic · Grey' }),
    );
    await user.click(screen.getByRole('button', { name: 'Done' }));
    expect(onNarrow).toHaveBeenCalledWith('req-1', {
      variantId: 'variant-2',
      filamentId: null,
      anyColor: false,
    });
  });

  it('names the default by material and file color, and picks any color of the material', async () => {
    const user = userEvent.setup();
    stubFetch({
      '/filaments': {
        filaments: [
          {
            id: 'filament-1',
            vendorName: 'Aliz',
            material: 'PETG Basic',
            materialType: 'PETG',
            variants: [],
          },
        ],
      },
    });
    const onNarrow = vi.fn();
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    render(
      <QueryClientProvider client={queryClient}>
        <PartFilamentRequirementsList
          requirements={[
            {
              key: 'req-1',
              materialType: 'PETG',
              detectedColorHex: '000000',
              detectedSubBrand: null,
            },
          ]}
          onNarrow={onNarrow}
        />
      </QueryClientProvider>,
    );
    await user.click(
      await screen.findByRole('button', {
        name: 'Production filament for PETG: Any PETG · #000000',
      }),
    );
    expect(
      screen.getByRole('radio', { name: 'Any PETG · #000000' }),
    ).toBeChecked();
    await user.click(
      screen.getByRole('radio', { name: 'Any PETG · Any color' }),
    );
    await user.click(screen.getByRole('button', { name: 'Done' }));
    expect(onNarrow).toHaveBeenCalledWith('req-1', {
      variantId: null,
      filamentId: null,
      anyColor: true,
    });
  });

  it('lets an already-narrowed requirement be cleared back to "None"', async () => {
    const user = userEvent.setup();
    stubFetch({
      '/profiles/filament/f1': { id: 'f1', name: 'Bambu PLA Basic @BBL X1C' },
      '/profiles/filament': { items: [] },
    });
    const onNarrowToProfile = vi.fn();

    renderList(
      [
        {
          key: 'req-1',
          materialType: 'PLA',
          detectedColorHex: '#ffffff',
          detectedSubBrand: null,
          narrowFilamentProfileId: 'f1',
        },
      ],
      onNarrowToProfile,
    );

    await waitFor(() =>
      expect(
        screen.getByRole('button', { name: 'Bambu PLA Basic @BBL X1C' }),
      ).toBeInTheDocument(),
    );

    await user.click(
      screen.getByRole('button', { name: 'Bambu PLA Basic @BBL X1C' }),
    );
    await user.click(screen.getByText('None'));

    expect(onNarrowToProfile).toHaveBeenCalledWith('req-1', null);
  });

  it('flags an unresolved narrowed profile', () => {
    stubFetch({ '/profiles/filament': { items: [] } });

    renderList(
      [
        {
          key: 'req-1',
          materialType: 'PLA',
          detectedColorHex: '#ffffff',
          detectedSubBrand: null,
          narrowFilamentProfileId: 'deleted-id',
          narrowFilamentProfileUnresolved: true,
        },
      ],
      vi.fn(),
    );

    expect(screen.getByText('Narrowed profile removed')).toBeInTheDocument();
  });

  it('shows each colour’s grams when known', () => {
    stubFetch({ '/profiles/filament': { items: [] } });

    renderList(
      [
        {
          key: 'req-1',
          materialType: 'PLA',
          detectedColorHex: '#000000',
          detectedSubBrand: null,
          estimatedGrams: 12.4,
        },
        {
          key: 'req-2',
          materialType: 'PLA',
          detectedColorHex: '#ffffff',
          detectedSubBrand: null,
        },
      ],
      vi.fn(),
    );

    expect(screen.getByText('12.4 g')).toBeInTheDocument();
    expect(screen.getAllByText(/ g$/)).toHaveLength(1);
  });
});
