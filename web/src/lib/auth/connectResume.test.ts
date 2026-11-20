import { afterEach, expect, it, vi } from 'vitest';
import {
  clearConnectRequest,
  connectReturnTo,
  resolveConnectRequest,
} from './connectResume';

afterEach(() => {
  vi.unstubAllGlobals();
  sessionStorage.clear();
});

it('keeps a long consent request out of the return path', () => {
  const request = 'x'.repeat(4000);
  const returnTo = connectReturnTo(`/connect?request=${request}`);
  expect(returnTo).toMatch(/^\/connect\?resume=[0-9a-f]{32}$/);
  const id = new URL(returnTo, 'http://x').searchParams.get('resume');
  expect(resolveConnectRequest(id)).toBe(request);
});

it('gives the same request the same id when called again', () => {
  expect(connectReturnTo('/connect?request=abc')).toBe(
    connectReturnTo('/connect?request=abc'),
  );
  expect(connectReturnTo('/connect?request=abc')).not.toBe(
    connectReturnTo('/connect?request=def'),
  );
});

it('leaves other paths and resume links alone', () => {
  expect(connectReturnTo('/printers?x=1')).toBe('/printers?x=1');
  expect(connectReturnTo('/connect?resume=abc')).toBe('/connect?resume=abc');
});

it('does not resolve an unknown id, and forgets after clearing', () => {
  const id = new URL(
    connectReturnTo('/connect?request=abc'),
    'http://x',
  ).searchParams.get('resume');
  expect(resolveConnectRequest('other')).toBeNull();
  expect(resolveConnectRequest(null)).toBeNull();
  clearConnectRequest();
  expect(resolveConnectRequest(id)).toBeNull();
});

it('falls back to the full path when storage is unavailable', () => {
  const blocked = () => {
    throw new Error('blocked');
  };
  vi.stubGlobal('sessionStorage', {
    getItem: blocked,
    setItem: blocked,
    removeItem: blocked,
  });
  expect(connectReturnTo('/connect?request=abc')).toBe('/connect?request=abc');
  expect(resolveConnectRequest('abc')).toBeNull();
});
