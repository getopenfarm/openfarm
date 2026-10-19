import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiUrl } from './apiUrl';

const paths = ['/api/v1/printers', '/hubs/farm', '/api/v1/print-jobs/123/file'];

afterEach(() => vi.unstubAllEnvs());

describe('API URLs', () => {
  it.each([undefined, ''])(
    'uses the current origin when the override is %s',
    (base) => {
      vi.stubEnv('VITE_API_BASE_URL', base);
      for (const path of paths) expect(apiUrl(path)).toBe(path);
    },
  );

  it('preserves a build-time override without duplicate slashes', () => {
    vi.stubEnv('VITE_API_BASE_URL', 'https://farm.example///');
    for (const path of paths) {
      expect(apiUrl(path)).toBe(`https://farm.example${path}`);
    }
  });
});
