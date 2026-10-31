import { afterEach, describe, expect, it, vi } from 'vitest';
import { authenticatedFetch, refreshSession } from './session';
import { clearToken, getToken, setToken } from './tokenStorage';

const json = (data: unknown, status = 200) =>
  new Response(JSON.stringify(data), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
afterEach(() => {
  clearToken();
  vi.unstubAllGlobals();
});
describe('session renewal', () => {
  it('shares one refresh across simultaneous requests and sends antiforgery proof', async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(json({ token: 'csrf' }))
      .mockResolvedValueOnce(json({ token: 'fresh' }));
    vi.stubGlobal('fetch', fetch);
    const results = await Promise.all([
      refreshSession(),
      refreshSession(),
      refreshSession(),
    ]);
    expect(results).toEqual(['fresh', 'fresh', 'fresh']);
    expect(fetch).toHaveBeenCalledTimes(2);
    expect(fetch).toHaveBeenLastCalledWith(
      '/api/v1/auth/refresh',
      expect.objectContaining({
        credentials: 'include',
        headers: { 'X-CSRF-TOKEN': 'csrf' },
      }),
    );
    expect(localStorage.getItem('openfarm.auth.token')).toBeNull();
  });
  it('refreshes and retries a rejected mutation exactly once', async () => {
    setToken('old');
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(json({}, 401))
      .mockResolvedValueOnce(json({ token: 'csrf' }))
      .mockResolvedValueOnce(json({ token: 'fresh' }))
      .mockResolvedValueOnce(json({ saved: true }));
    vi.stubGlobal('fetch', fetch);
    const result = await authenticatedFetch('/api/v1/parts', {
      method: 'POST',
      body: '{"name":"Part"}',
    });
    expect(result.status).toBe(200);
    expect(fetch).toHaveBeenCalledTimes(4);
    expect(fetch).toHaveBeenLastCalledWith(
      '/api/v1/parts',
      expect.objectContaining({
        method: 'POST',
        body: '{"name":"Part"}',
        headers: { Authorization: 'Bearer fresh' },
      }),
    );
  });
  it('preserves authentication when refresh fails with a network error', async () => {
    setToken('old');
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')));
    await expect(refreshSession()).rejects.toThrow('offline');
    expect(getToken()).toBe('old');
  });
  it('does not refresh on permission failures', async () => {
    setToken('old');
    const fetch = vi.fn().mockResolvedValue(json({}, 403));
    vi.stubGlobal('fetch', fetch);
    expect((await authenticatedFetch('/api/v1/admin/users')).status).toBe(403);
    expect(fetch).toHaveBeenCalledTimes(1);
    expect(getToken()).toBe('old');
  });
});
