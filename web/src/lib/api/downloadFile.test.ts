import { toast } from 'sonner';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { authenticatedFetch } from '@/lib/auth/session';
import { downloadFile } from './downloadFile';

vi.mock('@/lib/auth/session', () => ({ authenticatedFetch: vi.fn() }));
vi.mock('sonner', () => ({ toast: { error: vi.fn() } }));
afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  vi.clearAllMocks();
});

describe('downloadFile', () => {
  it.each([
    ['attachment; filename="source.3mf"', 'source.3mf'],
    [null, 'fallback.stl'],
  ])(
    'saves authenticated bytes with filename %s',
    async (disposition, expected) => {
      vi.mocked(authenticatedFetch).mockResolvedValue(
        new Response('source bytes', {
          headers: disposition ? { 'Content-Disposition': disposition } : {},
        }),
      );
      const create = vi.fn((_blob: Blob) => 'blob:source');
      const revoke = vi.fn();
      vi.stubGlobal(
        'URL',
        class extends URL {
          static createObjectURL = create;
          static revokeObjectURL = revoke;
        },
      );
      let savedName = '';
      vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(
        function (this: HTMLAnchorElement) {
          savedName = this.download;
        },
      );
      await downloadFile(
        '/api/v1/parts/part-1/file',
        'fallback.stl',
        'Could not download part file.',
      );
      expect(authenticatedFetch).toHaveBeenCalledWith(
        '/api/v1/parts/part-1/file',
      );
      expect(savedName).toBe(expected);
      expect(await create.mock.calls[0]?.[0]?.text()).toBe('source bytes');
      expect(revoke).toHaveBeenCalledWith('blob:source');
    },
  );
  it.each(['api', 'network', 'body'])('reports a %s failure', async (kind) => {
    if (kind === 'network')
      vi.mocked(authenticatedFetch).mockRejectedValue(new Error('offline'));
    else if (kind === 'body') {
      const response = new Response('bytes');
      vi.spyOn(response, 'blob').mockRejectedValue(new Error('interrupted'));
      vi.mocked(authenticatedFetch).mockResolvedValue(response);
    } else
      vi.mocked(authenticatedFetch).mockResolvedValue(
        new Response(JSON.stringify({ detail: 'Source file unavailable.' }), {
          status: 404,
        }),
      );
    await downloadFile('/file', 'part.stl', 'Could not download part file.');
    expect(toast.error).toHaveBeenCalledWith(
      kind === 'api'
        ? 'Source file unavailable.'
        : 'Could not download part file.',
    );
  });
});
