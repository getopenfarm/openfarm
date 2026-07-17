import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { Toaster } from 'sonner';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ImportProfilesDialog } from './ImportProfilesDialog';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function renderDialog() {
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

  return render(<ImportProfilesDialog />, { wrapper: Wrapper });
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

describe('ImportProfilesDialog', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('walks file select -> preview -> select subset -> confirm', async () => {
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

    renderDialog();
    await user.click(screen.getByRole('button', { name: 'Import' }));

    const dialog = await screen.findByRole('dialog');
    const input = dialog.querySelector(
      'input[type="file"]',
    ) as HTMLInputElement;
    const file = new File(['{}'], 'bundle.zip', { type: 'application/zip' });
    await user.upload(input, file);

    expect(await screen.findByText('Test Printer')).toBeInTheDocument();
    expect(screen.getByText('Test PLA')).toBeInTheDocument();

    // Uncheck the filament preset, keeping only the machine preset selected.
    const filamentCheckbox = screen.getByRole('checkbox', {
      name: 'Select Test PLA',
    });
    await user.click(filamentCheckbox);

    await user.click(screen.getByRole('button', { name: 'Confirm import' }));

    await waitFor(() => {
      const confirmCall = fetchMock.mock.calls.find(([reqInput]) =>
        (typeof reqInput === 'string'
          ? reqInput
          : reqInput.toString()
        ).includes('/imports/confirm'),
      );
      expect(confirmCall).toBeDefined();
    });

    const confirmCall = fetchMock.mock.calls.find(([reqInput]) =>
      (typeof reqInput === 'string' ? reqInput : reqInput.toString()).includes(
        '/imports/confirm',
      ),
    );
    expect(confirmCall).toBeDefined();
    const confirmInit = confirmCall?.[1] as RequestInit;
    const formData = confirmInit.body as FormData;
    const selections = JSON.parse(formData.get('selections') as string);
    expect(selections).toEqual([
      { type: 'Machine', name: 'Test Printer', duplicateAction: null },
    ]);
  });

  it('accepts a file dropped onto the dropzone', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn((_input: RequestInfo | URL, _init?: RequestInit) =>
      Promise.resolve(jsonResponse(previewResponseBody)),
    );
    vi.stubGlobal('fetch', fetchMock);

    renderDialog();
    await user.click(screen.getByRole('button', { name: 'Import' }));
    const dialog = await screen.findByRole('dialog');
    const dropzone = within(dialog).getByRole('button', {
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

    renderDialog();
    await user.click(screen.getByRole('button', { name: 'Import' }));
    const dialog = await screen.findByRole('dialog');
    const dropzone = within(dialog).getByRole('button', {
      name: 'Choose a profile bundle file, or drag and drop it here',
    });
    const input = dialog.querySelector(
      'input[type="file"]',
    ) as HTMLInputElement;
    const clickSpy = vi.spyOn(input, 'click');

    await user.click(dropzone);

    expect(clickSpy).toHaveBeenCalled();
  });

  it('closing before confirm leaves nothing selected on reopen', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn((_input: RequestInfo | URL, _init?: RequestInit) =>
      Promise.resolve(jsonResponse(previewResponseBody)),
    );
    vi.stubGlobal('fetch', fetchMock);

    renderDialog();
    await user.click(screen.getByRole('button', { name: 'Import' }));
    const dialog = await screen.findByRole('dialog');
    const input = dialog.querySelector(
      'input[type="file"]',
    ) as HTMLInputElement;
    const file = new File(['{}'], 'bundle.zip', { type: 'application/zip' });
    await user.upload(input, file);

    await screen.findByText('Test Printer');

    // Close without confirming (Escape closes a Radix dialog).
    await user.keyboard('{Escape}');

    await waitFor(() =>
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument(),
    );

    // Reopening starts a fresh, empty flow — no leftover selection or preview state.
    await user.click(screen.getByRole('button', { name: 'Import' }));
    const reopenedDialog = await screen.findByRole('dialog');
    expect(
      within(reopenedDialog).queryByText('Test Printer'),
    ).not.toBeInTheDocument();
    expect(
      reopenedDialog.querySelector('input[type="file"]'),
    ).toBeInTheDocument();

    const confirmCalls = fetchMock.mock.calls.filter(([reqInput]) =>
      (typeof reqInput === 'string' ? reqInput : reqInput.toString()).includes(
        '/imports/confirm',
      ),
    );
    expect(confirmCalls).toHaveLength(0);
  });
});
