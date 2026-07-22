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

  it('shows the ProfilePickerCombobox, origin-labeled, for a requirement with no narrowed profile yet', async () => {
    const user = userEvent.setup();
    stubFetch({
      '/profiles/filament': {
        items: [
          {
            id: 'f1',
            name: 'Bambu PLA Basic @BBL X1C',
            origin: 'System',
            vendorName: 'Bambulab',
          },
          { id: 'f2', name: 'My Tuned PLA', origin: 'User' },
        ],
      },
    });
    const onNarrowToProfile = vi.fn();

    renderList(
      [
        {
          key: 'req-1',
          materialType: 'PLA',
          detectedColorHex: '#ffffff',
          detectedSubBrand: null,
        },
      ],
      onNarrowToProfile,
    );

    await user.click(
      screen.getByRole('button', { name: 'Match by generic type only' }),
    );

    await waitFor(() =>
      expect(screen.getByText('Bambu PLA Basic @BBL X1C')).toBeInTheDocument(),
    );
    expect(screen.getByText('System: Bambulab')).toBeInTheDocument();
    expect(screen.getByText('My Tuned PLA')).toBeInTheDocument();

    await user.click(screen.getByText('Bambu PLA Basic @BBL X1C'));

    expect(onNarrowToProfile).toHaveBeenCalledWith('req-1', 'f1');
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

    expect(screen.getByText('Narrowed profile deleted')).toBeInTheDocument();
  });
});
