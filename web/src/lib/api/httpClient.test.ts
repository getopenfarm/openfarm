import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { clearToken, getToken, setToken } from '@/lib/auth/tokenStorage';
import { httpClient } from './httpClient';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('httpClient', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.unstubAllEnvs();
    clearToken();
  });

  it.each([undefined, '', 'https://farm.example/'])(
    'sends authenticated requests using API base %s',
    async (base) => {
      vi.stubEnv('VITE_API_BASE_URL', base);
      setToken('a-valid-token');
      const fetchMock = vi.fn().mockResolvedValue(jsonResponse({}));
      vi.stubGlobal('fetch', fetchMock);
      await httpClient('/api/v1/printers');
      expect(fetchMock).toHaveBeenCalledWith(
        `${base ? 'https://farm.example' : ''}/api/v1/printers`,
        expect.objectContaining({
          headers: expect.objectContaining({
            Authorization: 'Bearer a-valid-token',
          }),
        }),
      );
    },
  );

  it('does not set Content-Type on a bodyless request', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({}));
    vi.stubGlobal('fetch', fetchMock);

    await httpClient('/api/v1/activity-log?page=1&pageSize=25');

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    const headers = new Headers(init.headers);
    expect(headers.has('Content-Type')).toBe(false);
  });

  it('sets Content-Type: application/json when a body is present', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({}));
    vi.stubGlobal('fetch', fetchMock);

    await httpClient('/api/v1/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username: 'a', password: 'b' }),
    });

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    const headers = new Headers(init.headers);
    expect(headers.get('Content-Type')).toBe('application/json');
  });

  it('adds an Authorization header when a token is stored', async () => {
    setToken('a-valid-token');
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({}));
    vi.stubGlobal('fetch', fetchMock);

    await httpClient('/api/v1/auth/whoami');

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    const headers = new Headers(init.headers);
    expect(headers.get('Authorization')).toBe('Bearer a-valid-token');
  });

  it('throws the raw Response when the request fails', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({}, 401)));

    await expect(httpClient('/api/v1/auth/whoami')).rejects.toMatchObject({
      status: 401,
    });
  });

  describe('on a 401 response', () => {
    const originalLocation = window.location;
    let assignSpy: ReturnType<typeof vi.fn>;

    beforeEach(() => {
      assignSpy = vi.fn();
      Object.defineProperty(window, 'location', {
        configurable: true,
        value: {
          ...originalLocation,
          pathname: '/printers',
          assign: assignSpy,
        },
      });
    });

    afterEach(() => {
      Object.defineProperty(window, 'location', {
        configurable: true,
        value: originalLocation,
      });
    });

    it('clears the stored token and redirects to /login', async () => {
      setToken('a-stale-token');
      vi.stubGlobal(
        'fetch',
        vi
          .fn()
          .mockResolvedValueOnce(jsonResponse({}, 401))
          .mockResolvedValueOnce(jsonResponse({ token: 'csrf' }))
          .mockResolvedValueOnce(jsonResponse({}, 401)),
      );

      await expect(httpClient('/api/v1/printers')).rejects.toMatchObject({
        status: 401,
      });

      expect(getToken()).toBeNull();
      expect(assignSpy).toHaveBeenCalledWith('/login?returnTo=%2Fprinters');
    });

    it('does not redirect again when already on /login', async () => {
      setToken('a-stale-token');
      Object.defineProperty(window, 'location', {
        configurable: true,
        value: { ...originalLocation, pathname: '/login', assign: assignSpy },
      });
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({}, 401)));

      await expect(
        httpClient('/api/v1/auth/login', { method: 'POST' }),
      ).rejects.toMatchObject({ status: 401 });

      expect(assignSpy).not.toHaveBeenCalled();
    });
  });
});
