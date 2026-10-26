import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { createMemoryRouter, Link, Outlet, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { getOpenFarmApiEndpointsPartProcessingJobsGetPartProcessingJobEndpointQueryKey } from '@/lib/api/generated/part-processing-jobs/part-processing-jobs';
import { PartFormPage } from './PartFormPage';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const JOB_ID = 'job-1';

function jobBody(status: string, overrides?: Record<string, unknown>) {
  return {
    id: JOB_ID,
    kind: 'Add',
    status,
    partId: null,
    slicer: null,
    preview: null,
    errorMessage: null,
    ...overrides,
  };
}

function baseFetchMock(
  handleOther?: (url: string, init?: RequestInit) => Response | undefined,
) {
  return vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input.toString();
    if (url.includes('/hubs/farm')) {
      return Promise.reject(new Error('SignalR not available in tests'));
    }
    if (url.includes('/settings/slicing-defaults')) {
      return Promise.resolve(jsonResponse({ default: null, available: [] }));
    }
    if (url.endsWith('/api/v1/parts') && init?.method === 'POST') {
      return Promise.resolve(
        jsonResponse({ jobId: JOB_ID, status: 'Processing' }, 202),
      );
    }
    const handled = handleOther?.(url, init);
    if (handled) {
      return Promise.resolve(handled);
    }
    if (url.includes(`/part-processing-jobs/${JOB_ID}`)) {
      return Promise.resolve(jsonResponse(jobBody('Processing')));
    }
    return Promise.resolve(jsonResponse({}));
  });
}

function TestLayout() {
  return (
    <div>
      <Link to="/parts">Nav away</Link>
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
          { path: '/parts/new', element: <PartFormPage /> },
          { path: '/parts', element: <div>Parts list page</div> },
          { path: '/assemblies', element: <div>Assemblies page</div> },
        ],
      },
    ],
    { initialEntries: ['/parts/new'] },
  );

  render(<RouterProvider router={router} />, { wrapper: Wrapper });
  return { queryClient };
}

function uploadFile() {
  const fileInput = document.querySelector(
    'input[type="file"]',
  ) as HTMLInputElement;
  const file = new File(['solid test'], 'benchy.stl', {
    type: 'application/octet-stream',
  });
  return { fileInput, file };
}

/** The real page has no polling — job status updates arrive as a SignalR push written directly
 * into the query cache (usePartsHub). Tests simulate that push the same way. */
function pushJobStatus(queryClient: QueryClient, status: string) {
  act(() => {
    queryClient.setQueryData(
      getOpenFarmApiEndpointsPartProcessingJobsGetPartProcessingJobEndpointQueryKey(
        JOB_ID,
      ),
      { data: jobBody(status), status: 200, headers: new Headers() },
    );
  });
}

describe('PartFormPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('starts background processing on file upload and lets name/tags be edited while it continues', async () => {
    const user = userEvent.setup();
    vi.stubGlobal('fetch', baseFetchMock());

    renderRoute();

    const { fileInput, file } = uploadFile();
    await user.upload(fileInput, file);

    expect(await screen.findByText('benchy.stl')).toBeInTheDocument();
    expect(screen.getByText('Processing…')).toBeInTheDocument();

    await user.type(screen.getByLabelText('Name'), 'Widget Bracket');
    await user.type(screen.getByLabelText('Tags'), 'Brackets');

    expect(screen.getByLabelText('Name')).toHaveValue('Widget Bracket');
    expect(screen.getByText('Processing…')).toBeInTheDocument();
  });

  it('confirms once ready, shows a success toast, and navigates to /parts', async () => {
    const user = userEvent.setup();
    vi.stubGlobal(
      'fetch',
      baseFetchMock((url, init) => {
        if (
          url.includes(`/part-processing-jobs/${JOB_ID}/confirm`) &&
          init?.method === 'POST'
        ) {
          return jsonResponse({});
        }
        return undefined;
      }),
    );

    const { queryClient } = renderRoute();

    const { fileInput, file } = uploadFile();
    await user.upload(fileInput, file);

    await user.type(await screen.findByLabelText('Name'), 'Widget Bracket');

    pushJobStatus(queryClient, 'ReadyForReview');

    const submitButton = await screen.findByRole('button', {
      name: 'Add part',
    });
    await waitFor(() => expect(submitButton).not.toBeDisabled());
    await user.click(submitButton);

    expect(await screen.findByText('Parts list page')).toBeInTheDocument();
  });

  it('discards the pending job when unsaved changes are confirmed away', async () => {
    const user = userEvent.setup();
    let discardCalled = false;
    vi.stubGlobal(
      'fetch',
      baseFetchMock((url, init) => {
        if (
          url.includes(`/part-processing-jobs/${JOB_ID}/confirm`) &&
          init?.method === 'DELETE'
        ) {
          discardCalled = true;
          return jsonResponse({});
        }
        return undefined;
      }),
    );

    renderRoute();

    const { fileInput, file } = uploadFile();
    await user.upload(fileInput, file);
    await screen.findByText('benchy.stl');

    await user.click(screen.getByRole('link', { name: 'Nav away' }));

    const dialog = await screen.findByRole('alertdialog');
    expect(
      within(dialog).getByText('Discard unsaved changes?'),
    ).toBeInTheDocument();
    await user.click(
      within(dialog).getByRole('button', { name: 'Discard changes' }),
    );

    expect(await screen.findByText('Parts list page')).toBeInTheDocument();
    await waitFor(() => expect(discardCalled).toBe(true));
  });

  it('adds one part per chosen plate and groups them as an assembly', async () => {
    const user = userEvent.setup();
    let confirmBody: unknown;
    vi.stubGlobal(
      'fetch',
      baseFetchMock((url, init) => {
        if (
          url.includes(`/part-processing-jobs/${JOB_ID}/confirm`) &&
          init?.method === 'POST'
        ) {
          confirmBody = JSON.parse(String(init.body));
          return jsonResponse({});
        }
        return undefined;
      }),
    );
    const { queryClient } = renderRoute();
    const { fileInput, file } = uploadFile();
    await user.upload(fileInput, file);
    await user.type(await screen.findByLabelText('Name'), 'Kit');

    const plate = (number: number, name: string | null) => ({
      number,
      name,
      estimatedPrintTimeMinutes: 10,
      estimatedFilamentWeightGrams: 4,
      filamentRequirements: [],
    });
    act(() => {
      queryClient.setQueryData(
        getOpenFarmApiEndpointsPartProcessingJobsGetPartProcessingJobEndpointQueryKey(
          JOB_ID,
        ),
        {
          data: jobBody('ReadyForReview', {
            preview: {
              estimateStatus: 'Available',
              filamentRequirements: [],
              plates: [plate(1, 'Cover'), plate(2, null)],
            },
          }),
          status: 200,
          headers: new Headers(),
        },
      );
    });

    await user.click(
      await screen.findByRole('button', { name: /Plate 1 · Cover/ }),
    );
    await user.click(screen.getByRole('button', { name: /Plate 2/ }));
    await user.click(screen.getByLabelText('Create assembly'));
    await user.click(screen.getByRole('button', { name: 'Add 2 parts' }));

    expect(await screen.findByText('Assemblies page')).toBeInTheDocument();
    expect(confirmBody).toMatchObject({
      name: 'Kit',
      plateNumbers: [1, 2],
      createAssembly: true,
    });
  });
});
