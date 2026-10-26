import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { AddOrReplacePartDialog } from './AddOrReplacePartDialog';

const PART_ID = '11111111-1111-1111-1111-111111111111';

function renderDialog(
  props: Partial<React.ComponentProps<typeof AddOrReplacePartDialog>> = {},
) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          {children}
          <Toaster />
        </MemoryRouter>
      </QueryClientProvider>
    );
  }

  return render(
    <AddOrReplacePartDialog
      partId={PART_ID}
      partName="Widget Bracket"
      open
      onOpenChange={() => {}}
      {...props}
    />,
    { wrapper: Wrapper },
  );
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const slicingDefaults = {
  default: { engine: 'OrcaSlicer', version: '2.2.0', isExplicit: false },
  available: [
    { engine: 'OrcaSlicer', version: '2.2.0', platform: 'Linux' },
    { engine: 'SuperSlicer', version: '2.5.59.13', platform: 'Linux' },
  ],
};

describe('AddOrReplacePartDialog (replace mode)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('titles the dialog for the part being replaced and never shows name/tags fields', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(slicingDefaults));
    vi.stubGlobal('fetch', fetchMock);

    renderDialog();

    expect(
      await screen.findByText('Replace file for Widget Bracket'),
    ).toBeInTheDocument();
    expect(screen.queryByLabelText('Name')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Tags')).not.toBeInTheDocument();
  });

  it('defaults the slicer picker to the farm default', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(slicingDefaults));
    vi.stubGlobal('fetch', fetchMock);

    renderDialog();

    expect(
      await screen.findByText('Farm default (OrcaSlicer 2.2.0)'),
    ).toBeInTheDocument();
  });

  it('sends the chosen slicer override with the upload, against the replace-file endpoint', async () => {
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/settings/slicing-defaults')) {
        return Promise.resolve(jsonResponse(slicingDefaults));
      }
      if (
        url.includes(`/api/v1/parts/${PART_ID}/file`) &&
        init?.method === 'PUT'
      ) {
        return Promise.resolve(
          jsonResponse({ jobId: 'job-1', status: 'Processing' }, 202),
        );
      }
      if (url.includes('/part-processing-jobs/')) {
        return Promise.resolve(
          jsonResponse({
            id: 'job-1',
            kind: 'Replace',
            status: 'Processing',
            partId: PART_ID,
            slicer: null,
            preview: null,
            errorMessage: null,
          }),
        );
      }
      return Promise.resolve(jsonResponse({}));
    });
    vi.stubGlobal('fetch', fetchMock);

    const user = userEvent.setup();
    renderDialog();

    await screen.findByText('Farm default (OrcaSlicer 2.2.0)');

    await user.click(screen.getByRole('combobox'));
    await user.click(
      await screen.findByRole('option', { name: 'SuperSlicer 2.5.59.13' }),
    );

    const file = new File(['solid test'], 'benchy.stl', {
      type: 'application/octet-stream',
    });
    // The dialog content is portaled to document.body by Radix, so it lives outside the
    // render() container — query the whole document instead.
    const fileInput = document.querySelector(
      'input[type="file"]',
    ) as HTMLInputElement;
    await user.upload(fileInput, file);

    await waitFor(() => {
      const postCall = fetchMock.mock.calls.find(([reqInput, reqInit]) => {
        const reqUrl =
          typeof reqInput === 'string' ? reqInput : reqInput.toString();
        return (
          reqUrl.includes(`/api/v1/parts/${PART_ID}/file`) &&
          reqInit?.method === 'PUT'
        );
      });
      expect(postCall).toBeDefined();
    });

    const putCall = fetchMock.mock.calls.find(([reqInput, reqInit]) => {
      const reqUrl =
        typeof reqInput === 'string' ? reqInput : reqInput.toString();
      return (
        reqUrl.includes(`/api/v1/parts/${PART_ID}/file`) &&
        reqInit?.method === 'PUT'
      );
    });
    if (!putCall) {
      throw new Error('Expected a PUT call to the replace-file endpoint');
    }
    const body = (putCall[1] as RequestInit).body as FormData;
    expect(body.get('slicerEngine')).toBe('SuperSlicer');
    expect(body.get('slicerVersion')).toBe('2.5.59.13');
  });
});

describe('AddOrReplacePartDialog (a part keeps its slicer)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const bambu = { engine: 'BambuStudio', version: '2.7.1' };
  const withBambu = {
    ...slicingDefaults,
    available: [
      ...slicingDefaults.available,
      { engine: 'BambuStudio', version: '2.7.1', platform: 'Linux' },
    ],
  };

  function stubFetch(
    extra?: (url: string, init?: RequestInit) => Response | undefined,
  ) {
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      const custom = extra?.(url, init);
      if (custom) return Promise.resolve(custom);
      if (url.includes('/settings/slicing-defaults')) {
        return Promise.resolve(jsonResponse(withBambu));
      }
      if (url.includes('/api/v1/slicing/readiness')) {
        return Promise.resolve(
          jsonResponse({
            engine: 'BambuStudio',
            version: '2.7.1',
            isReady: false,
            printers: [
              {
                printerId: 'p1',
                printerName: 'P1S Lab',
                issues: [
                  {
                    kind: 'Process',
                    code: 'Missing',
                    message:
                      "Process profile '0.20mm Standard @BBL X1C' is not in Bambu Studio 2.7.1.",
                    link: '/printers/p1/edit',
                  },
                ],
              },
            ],
            filaments: [],
          }),
        );
      }
      return Promise.resolve(jsonResponse({}));
    });
    vi.stubGlobal('fetch', fetchMock);
    return fetchMock;
  }

  it('offers to keep the current slicer instead of the farm default, and warns about what it cannot print', async () => {
    const fetchMock = stubFetch();

    renderDialog({ currentSlicer: bambu });

    expect(
      await screen.findByText('Keep current slicer (Bambu Studio 2.7.1)'),
    ).toBeInTheDocument();
    expect(screen.queryByText(/Farm default/)).not.toBeInTheDocument();
    expect(
      await screen.findByText('Production is blocked with Bambu Studio 2.7.1'),
    ).toBeInTheDocument();
    expect(fetchMock.mock.calls.map(([url]) => String(url))).toContain(
      '/api/v1/slicing/readiness?engine=BambuStudio&version=2.7.1',
    );
  });

  it('reprocesses with the part’s own slicer unless another is chosen', async () => {
    const fetchMock = stubFetch((url, init) =>
      url.includes(`/api/v1/parts/${PART_ID}/reprocess`) &&
      init?.method === 'POST'
        ? jsonResponse({ jobId: 'job-9', status: 'Processing' }, 202)
        : undefined,
    );
    const user = userEvent.setup();
    renderDialog({ mode: 'reprocess', currentSlicer: bambu });

    await user.click(
      await screen.findByRole('button', { name: 'Start reprocessing' }),
    );

    await waitFor(() => {
      const call = fetchMock.mock.calls.find(
        ([url, init]) =>
          String(url).includes('/reprocess') && init?.method === 'POST',
      );
      expect(call).toBeDefined();
      expect(
        JSON.parse(String((call?.[1] as RequestInit | undefined)?.body)),
      ).toEqual({});
    });
  });

  it('reprocesses on the slicer the operator picks', async () => {
    const fetchMock = stubFetch((url, init) =>
      url.includes(`/api/v1/parts/${PART_ID}/reprocess`) &&
      init?.method === 'POST'
        ? jsonResponse({ jobId: 'job-9', status: 'Processing' }, 202)
        : undefined,
    );
    const user = userEvent.setup();
    renderDialog({ mode: 'reprocess', currentSlicer: bambu });

    await user.click(await screen.findByRole('combobox'));
    await user.click(
      await screen.findByRole('option', { name: 'OrcaSlicer 2.2.0' }),
    );
    await user.click(
      screen.getByRole('button', { name: 'Start reprocessing' }),
    );

    await waitFor(() => {
      const call = fetchMock.mock.calls.find(
        ([url, init]) =>
          String(url).includes('/reprocess') && init?.method === 'POST',
      );
      expect(call).toBeDefined();
      expect(
        JSON.parse(String((call?.[1] as RequestInit | undefined)?.body)),
      ).toEqual({
        slicerEngine: 'OrcaSlicer',
        slicerVersion: '2.2.0',
      });
    });
  });

  it('replaces the parts sharing the file too, each matched to its plate by name', async () => {
    const user = userEvent.setup();
    let confirmBody: unknown;
    const plate = (number: number, name: string) => ({
      number,
      name,
      estimatedPrintTimeMinutes: 10,
      estimatedFilamentWeightGrams: 4,
      filamentRequirements: [],
    });
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
        const url = typeof input === 'string' ? input : input.toString();
        if (url.includes('/settings/slicing-defaults'))
          return Promise.resolve(jsonResponse(slicingDefaults));
        if (url.includes(`/api/v1/parts/${PART_ID}/file`))
          return Promise.resolve(
            jsonResponse({ jobId: 'job-1', status: 'Processing' }, 202),
          );
        if (url.includes('/confirm') && init?.method === 'POST') {
          confirmBody = JSON.parse(String(init.body));
          return Promise.resolve(jsonResponse({}));
        }
        if (url.includes('/part-processing-jobs/'))
          // The new file swaps the plates: Handle is plate 1, Cover plate 2.
          return Promise.resolve(
            jsonResponse({
              id: 'job-1',
              kind: 'Replace',
              status: 'ReadyForReview',
              partId: PART_ID,
              preview: {
                estimateStatus: 'Available',
                filamentRequirements: [],
                plates: [plate(1, 'Handle'), plate(2, 'Cover')],
              },
              relatedParts: [
                {
                  partId: 'handle-part',
                  name: 'Kit – Handle',
                  plateNumber: 2,
                  plateName: 'Handle',
                },
              ],
            }),
          );
        return Promise.resolve(jsonResponse({}));
      }),
    );

    renderDialog({ currentPlateNumber: 1, currentPlateName: 'Cover' });
    const fileInput = document.querySelector(
      'input[type="file"]',
    ) as HTMLInputElement;
    await user.upload(
      fileInput,
      new File(['zip'], 'kit-v2.gcode.3mf', { type: 'application/zip' }),
    );

    const save = await screen.findByRole('button', { name: 'Replace 2 parts' });
    expect(screen.queryByText(/Plate changes/)).not.toBeInTheDocument();
    expect(
      screen.getByRole('combobox', { name: 'Plate for Kit – Handle' }),
    ).toHaveTextContent('Plate 1 · Handle');
    await user.click(save);

    await waitFor(() =>
      expect(confirmBody).toMatchObject({
        plateNumbers: [2],
        relatedParts: [{ partId: 'handle-part', plateNumber: 1 }],
      }),
    );
  });
});
