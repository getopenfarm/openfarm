import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { AddOrReplacePartDialog } from './AddOrReplacePartDialog';

const PART_ID = '11111111-1111-1111-1111-111111111111';

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

  return render(
    <AddOrReplacePartDialog
      partId={PART_ID}
      partName="Widget Bracket"
      open
      onOpenChange={() => {}}
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

  it('defaults the slicer picker to the organization default', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(slicingDefaults));
    vi.stubGlobal('fetch', fetchMock);

    renderDialog();

    expect(
      await screen.findByText('Organization default (OrcaSlicer 2.2.0)'),
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

    await screen.findByText('Organization default (OrcaSlicer 2.2.0)');

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
