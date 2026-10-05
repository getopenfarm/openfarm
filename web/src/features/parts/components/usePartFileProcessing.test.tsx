import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { usePartFileProcessing } from './usePartFileProcessing';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function makeFile(name = 'benchy.stl') {
  return new File(['solid test'], name, { type: 'application/octet-stream' });
}

function renderHookWithClient<T>(callback: () => T) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );
  }
  return { ...renderHook(callback, { wrapper: Wrapper }), queryClient };
}

describe('usePartFileProcessing', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('mode="add": processing a file POSTs to /api/v1/parts and stores the returned jobId', async () => {
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/settings/slicing-defaults')) {
        return Promise.resolve(jsonResponse({ default: null, available: [] }));
      }
      if (url.endsWith('/api/v1/parts') && init?.method === 'POST') {
        return Promise.resolve(
          jsonResponse({ jobId: 'job-add-1', status: 'Processing' }, 202),
        );
      }
      return Promise.resolve(
        jsonResponse({
          id: 'job-add-1',
          kind: 'Add',
          status: 'Processing',
          partId: null,
          slicer: null,
          preview: null,
          errorMessage: null,
        }),
      );
    });
    vi.stubGlobal('fetch', fetchMock);

    const { result } = renderHookWithClient(() =>
      usePartFileProcessing({ mode: 'add' }),
    );

    act(() => {
      result.current.processFile(makeFile());
    });

    await waitFor(() => expect(result.current.jobId).toBe('job-add-1'));
    expect(
      fetchMock.mock.calls.some(
        (call) =>
          call[0].toString().endsWith('/api/v1/parts') &&
          (call[1] as RequestInit | undefined)?.method === 'POST',
      ),
    ).toBe(true);
  });

  it('mode="replace": processing a file PUTs to /api/v1/parts/:id/file', async () => {
    const partId = 'part-1';
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/settings/slicing-defaults')) {
        return Promise.resolve(jsonResponse({ default: null, available: [] }));
      }
      if (
        url.includes(`/api/v1/parts/${partId}/file`) &&
        init?.method === 'PUT'
      ) {
        return Promise.resolve(
          jsonResponse({ jobId: 'job-replace-1', status: 'Processing' }, 202),
        );
      }
      return Promise.resolve(
        jsonResponse({
          id: 'job-replace-1',
          kind: 'Replace',
          status: 'Processing',
          partId,
          slicer: null,
          preview: null,
          errorMessage: null,
        }),
      );
    });
    vi.stubGlobal('fetch', fetchMock);

    const { result } = renderHookWithClient(() =>
      usePartFileProcessing({ mode: 'replace', partId }),
    );

    act(() => {
      result.current.processFile(makeFile());
    });

    await waitFor(() => expect(result.current.jobId).toBe('job-replace-1'));
    expect(
      fetchMock.mock.calls.some(
        (call) =>
          call[0].toString().includes(`/api/v1/parts/${partId}/file`) &&
          (call[1] as RequestInit | undefined)?.method === 'PUT',
      ),
    ).toBe(true);
  });

  it('confirm() posts filament requirement overrides and calls onConfirmed after resetting state', async () => {
    let confirmedBody: unknown;
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/settings/slicing-defaults')) {
        return Promise.resolve(jsonResponse({ default: null, available: [] }));
      }
      if (url.endsWith('/api/v1/parts') && init?.method === 'POST') {
        return Promise.resolve(
          jsonResponse({ jobId: 'job-1', status: 'Processing' }, 202),
        );
      }
      if (
        url.includes('/part-processing-jobs/job-1/confirm') &&
        init?.method === 'POST'
      ) {
        confirmedBody = JSON.parse(init.body as string);
        return Promise.resolve(jsonResponse({}));
      }
      return Promise.resolve(
        jsonResponse({
          id: 'job-1',
          kind: 'Add',
          status: 'Processing',
          partId: null,
          slicer: null,
          preview: null,
          errorMessage: null,
        }),
      );
    });
    vi.stubGlobal('fetch', fetchMock);

    const onConfirmed = vi.fn();
    const { result } = renderHookWithClient(() =>
      usePartFileProcessing({ mode: 'add', onConfirmed }),
    );

    act(() => {
      result.current.processFile(makeFile());
    });
    await waitFor(() => expect(result.current.jobId).toBe('job-1'));

    act(() => {
      result.current.handleNarrow(0, 'variant-1');
    });

    act(() => {
      result.current.confirm({ name: 'Widget Bracket', tags: 'a, b' });
    });

    await waitFor(() => expect(onConfirmed).toHaveBeenCalledTimes(1));
    expect(confirmedBody).toEqual({
      name: 'Widget Bracket',
      tags: ['a', 'b'],
      filamentRequirementOverrides: [
        { requirementIndex: 0, narrowFilamentVariantId: 'variant-1' },
      ],
    });
    // Confirming resets in-flight state so a subsequent file drop starts clean.
    expect(result.current.file).toBeNull();
    expect(result.current.jobId).toBeNull();
  });

  it('cancel() discards an in-flight job that has not reached a terminal state', async () => {
    let discardCalled = false;
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/settings/slicing-defaults')) {
        return Promise.resolve(jsonResponse({ default: null, available: [] }));
      }
      if (url.endsWith('/api/v1/parts') && init?.method === 'POST') {
        return Promise.resolve(
          jsonResponse({ jobId: 'job-1', status: 'Processing' }, 202),
        );
      }
      if (
        url.includes('/part-processing-jobs/job-1/confirm') &&
        init?.method === 'DELETE'
      ) {
        discardCalled = true;
        return Promise.resolve(jsonResponse({}));
      }
      return Promise.resolve(
        jsonResponse({
          id: 'job-1',
          kind: 'Add',
          status: 'Processing',
          partId: null,
          slicer: null,
          preview: null,
          errorMessage: null,
        }),
      );
    });
    vi.stubGlobal('fetch', fetchMock);

    const { result } = renderHookWithClient(() =>
      usePartFileProcessing({ mode: 'add' }),
    );

    act(() => {
      result.current.processFile(makeFile());
    });
    await waitFor(() => expect(result.current.jobId).toBe('job-1'));

    act(() => {
      result.current.cancel();
    });

    await waitFor(() => expect(discardCalled).toBe(true));
    expect(result.current.file).toBeNull();
    expect(result.current.jobId).toBeNull();
  });
});

describe('explicit reprocessing', () => {
  afterEach(() => vi.unstubAllGlobals());
  it('starts from the stored file and can confirm an unavailable estimate', async () => {
    const confirmed = vi.fn();
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      if (url.endsWith('/reprocess'))
        return Promise.resolve(
          jsonResponse({ jobId: 'retry-1', status: 'Processing' }, 202),
        );
      if (url.endsWith('/confirm') && init?.method === 'POST')
        return Promise.resolve(jsonResponse({ id: 'part-1' }, 201));
      if (url.includes('/part-processing-jobs/'))
        return Promise.resolve(
          jsonResponse({
            id: 'retry-1',
            status: 'ReadyForReview',
            kind: 'Reprocess',
            partId: 'part-1',
            preview: {
              estimateStatus: 'Unavailable',
              nozzleDiameterMm: null,
              filamentRequirements: [],
            },
          }),
        );
      return Promise.resolve(jsonResponse({ available: [], default: null }));
    });
    vi.stubGlobal('fetch', fetchMock);
    const { result } = renderHookWithClient(() =>
      usePartFileProcessing({
        mode: 'reprocess',
        partId: 'part-1',
        onConfirmed: confirmed,
      }),
    );
    expect(
      fetchMock.mock.calls.some((call) =>
        call[0].toString().endsWith('/reprocess'),
      ),
    ).toBe(false);
    act(() => result.current.reprocess());
    await waitFor(() => expect(result.current.status).toBe('ReadyForReview'));
    expect(result.current.file).toBeNull();
    act(() => result.current.confirm());
    await waitFor(() => expect(confirmed).toHaveBeenCalledOnce());
    expect(
      fetchMock.mock.calls.find((call) =>
        call[0].toString().endsWith('/reprocess'),
      )?.[1]?.body,
    ).toBe('{}');
  });
});
