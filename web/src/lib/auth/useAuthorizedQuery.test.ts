import { describe, expect, it } from 'vitest';
import { queryIsAllowed } from './useAuthorizedQuery';

describe('generated query authorization', () => {
  it('requires explicit read permission before fetching protected data', () => {
    expect(queryIsAllowed(['/api/v1/printers'], new Set())).toBe(false);
    expect(
      queryIsAllowed(['/api/v1/printers'], new Set(['printers:list'])),
    ).toBe(true);
    expect(queryIsAllowed(['/api/v1/auth/whoami'], new Set())).toBe(true);
  });
  it('requires all permissions for mixed notification content', () => {
    expect(
      queryIsAllowed(['/api/v1/notifications'], new Set(['print-jobs:view'])),
    ).toBe(false);
    expect(
      queryIsAllowed(
        ['/api/v1/notifications'],
        new Set(['print-jobs:view', 'spools:list', 'filaments:list']),
      ),
    ).toBe(true);
  });
  it('matches static routes before entity routes and handles library scope', () => {
    expect(
      queryIsAllowed(
        ['/api/v1/printers/catalog'],
        new Set(['printers:get-detail']),
      ),
    ).toBe(false);
    expect(
      queryIsAllowed(
        ['/api/v1/library/parts/folders'],
        new Set(['library:view']),
      ),
    ).toBe(false);
    expect(
      queryIsAllowed(
        ['/api/v1/library/parts/folders'],
        new Set(['library:view', 'parts:list']),
      ),
    ).toBe(true);
  });
});
