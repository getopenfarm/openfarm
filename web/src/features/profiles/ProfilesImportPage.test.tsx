import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { createMemoryRouter, Link, Outlet, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { ProfilesImportPage } from './ProfilesImportPage';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const previewResponseBody = {
  sourceFileName: 'bundle.zip',
  machinePresets: [
    {
      name: 'Test Printer',
      sourceFormat: 'OrcaSlicer',
      baseProfileName: null,
      hasUnresolvedBaseInChain: false,
      isDuplicate: false,
      nozzleSizeMm: 0.4,
      bedSizeXMm: null,
      bedSizeYMm: null,
      maxHeightMm: null,
      hasEnclosure: null,
      supportedMaterials: [],
    },
  ],
  filamentPresets: [
    {
      name: 'Test PLA',
      sourceFormat: 'OrcaSlicer',
      baseProfileName: null,
      hasUnresolvedBaseInChain: false,
      isDuplicate: false,
      material: 'PLA',
      brand: null,
      colorHex: null,
      nozzleSizeMm: null,
      nozzleTempC: null,
      bedTempC: null,
      maxVolumetricSpeedMm3s: null,
      fanPercent: null,
      compatiblePrinterNames: [],
    },
  ],
};

function TestLayout() {
  return (
    <div>
      <Link to="/profiles">Nav away</Link>
      <Outlet />
    </div>
  );
}

function renderRoute() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        {children}
        <Toaster />
      </QueryClientProvider>
    );
  }

  const router = createMemoryRouter(
    [
      {
        element: <TestLayout />,
        children: [
          { path: '/profiles/import', element: <ProfilesImportPage /> },
          { path: '/profiles', element: <div>Profiles list page</div> },
        ],
      },
    ],
    { initialEntries: ['/profiles/import'] },
  );

  render(<RouterProvider router={router} />, { wrapper: Wrapper });
}

describe('ProfilesImportPage', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('walks file select -> preview -> select subset -> confirm, shows a toast, and navigates to /profiles', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn((input: RequestInfo | URL, _init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/imports/preview')) {
        return Promise.resolve(jsonResponse(previewResponseBody));
      }
      if (url.includes('/imports/confirm')) {
        return Promise.resolve(
          jsonResponse(
            {
              createdMachineProfiles: [
                { id: '1', name: 'Test Printer', baseProfileId: null },
              ],
              createdFilamentProfiles: [],
            },
            201,
          ),
        );
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderRoute();

    const fileInput = document.querySelector(
      'input[type="file"]',
    ) as HTMLInputElement;
    const file = new File(['{}'], 'bundle.zip', { type: 'application/zip' });
    await user.upload(fileInput, file);

    expect(await screen.findByText('Test Printer')).toBeInTheDocument();
    expect(screen.getByText('Test PLA')).toBeInTheDocument();

    // Uncheck the filament preset, keeping the machine preset selected.
    const filamentCheckbox = screen.getByRole('checkbox', {
      name: 'Select Test PLA',
    });
    await user.click(filamentCheckbox);

    await user.click(screen.getByRole('button', { name: 'Confirm import' }));

    expect(await screen.findByText('Profiles list page')).toBeInTheDocument();

    const confirmCall = fetchMock.mock.calls.find(([reqInput]) =>
      (typeof reqInput === 'string' ? reqInput : reqInput.toString()).includes(
        '/imports/confirm',
      ),
    );
    if (!confirmCall) {
      throw new Error('Expected a confirm-import call');
    }
    const confirmInit = confirmCall[1] as RequestInit;
    const formData = confirmInit.body as FormData;
    const selections = JSON.parse(formData.get('selections') as string);
    expect(selections).toEqual([
      { type: 'Machine', name: 'Test Printer', duplicateAction: null },
    ]);
  });

  it('accepts a file dropped onto the dropzone', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(jsonResponse(previewResponseBody))),
    );

    renderRoute();

    const dropzone = screen.getByRole('button', {
      name: 'Choose a profile bundle file, or drag and drop it here',
    });

    const file = new File(['{}'], 'bundle.zip', { type: 'application/zip' });
    const dataTransfer = { files: [file] } as unknown as DataTransfer;
    fireEvent.dragOver(dropzone, { dataTransfer });
    fireEvent.drop(dropzone, { dataTransfer });

    expect(await screen.findByText('Test Printer')).toBeInTheDocument();
    expect(screen.getByText('Test PLA')).toBeInTheDocument();
  });

  it('clicking the dropzone opens the native file picker', async () => {
    const user = userEvent.setup();
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(jsonResponse(previewResponseBody))),
    );

    renderRoute();

    const dropzone = screen.getByRole('button', {
      name: 'Choose a profile bundle file, or drag and drop it here',
    });
    const fileInput = document.querySelector(
      'input[type="file"]',
    ) as HTMLInputElement;
    const clickSpy = vi.spyOn(fileInput, 'click');

    await user.click(dropzone);

    expect(clickSpy).toHaveBeenCalled();
  });

  it('blocks in-app navigation away while a file is selected', async () => {
    const user = userEvent.setup();
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(jsonResponse(previewResponseBody))),
    );

    renderRoute();

    const fileInput = document.querySelector(
      'input[type="file"]',
    ) as HTMLInputElement;
    const file = new File(['{}'], 'bundle.zip', { type: 'application/zip' });
    await user.upload(fileInput, file);

    await screen.findByText('Test Printer');

    await user.click(screen.getByRole('link', { name: 'Nav away' }));

    const dialog = await screen.findByRole('alertdialog');
    expect(
      within(dialog).getByText('Discard unsaved changes?'),
    ).toBeInTheDocument();
  });
});
