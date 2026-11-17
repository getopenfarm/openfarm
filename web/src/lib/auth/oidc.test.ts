import { describe, expect, it } from 'vitest';
import { localReturnTo } from './oidc';

describe('OIDC return destinations', () => {
  it.each([
    null,
    'https://evil.example',
    '//evil.example',
    '/\\evil.example',
    '/\n/evil.example',
    '/api/v1/auth/sso/login',
    '/auth/complete',
  ])('rejects %j', (value) => {
    expect(localReturnTo(value)).toBe('/');
  });
  it('preserves an MCP consent destination', () => {
    expect(localReturnTo('/connect?request=abc')).toBe('/connect?request=abc');
  });
});
