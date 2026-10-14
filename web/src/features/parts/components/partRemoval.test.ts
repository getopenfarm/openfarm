import { afterEach, describe, expect, it, vi } from 'vitest';
import { checkPartDeletion, removalError } from './partRemoval';

function response(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}
afterEach(() => vi.unstubAllGlobals());
describe('part removal transport', () => {
  it.each([429, 500, 503])(
    'allows rechecked retries for HTTP %s',
    async (status) => {
      expect(
        (await removalError(response({ detail: 'Try later' }, status)))
          .retryable,
      ).toBe(true);
    },
  );
  it.each([400, 403, 404, 409, 422])(
    'does not classify HTTP %s as a retryable request failure',
    async (status) => {
      expect((await removalError(response(null, status))).retryable).toBe(
        false,
      );
    },
  );
  it('distinguishes network errors from unknown exceptions', async () => {
    expect(
      (await removalError(new TypeError('Failed to fetch'))).retryable,
    ).toBe(true);
    expect(
      (await removalError(new Error('Unexpected exception'))).retryable,
    ).toBe(false);
  });
  it.each([
    {},
    { parts: [] },
    { parts: [null] },
    {
      parts: [
        {
          partId: 'a',
          status: 'Eligible',
          blockers: [],
          legacyJobsLosingRecovery: -1,
        },
      ],
    },
    {
      parts: [
        {
          partId: 'other',
          status: 'Eligible',
          blockers: [],
          legacyJobsLosingRecovery: 0,
        },
      ],
    },
    {
      parts: [
        {
          partId: 'a',
          status: 'Eligible',
          blockers: [null],
          legacyJobsLosingRecovery: 0,
        },
      ],
    },
  ])('rejects incomplete or malformed previews', async (body) => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => response(body)),
    );
    await expect(checkPartDeletion(['a'])).rejects.toThrow(
      'Invalid deletion check response',
    );
  });
});
