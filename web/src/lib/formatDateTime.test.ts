import { expect, it } from 'vitest';
import { formatDateTime } from './formatDateTime';

it('formats ISO strings and Dates identically', () => {
  const iso = '2026-10-16T12:00:00Z';
  expect(formatDateTime(iso)).toBe(formatDateTime(new Date(iso)));
  expect(formatDateTime(iso)).toMatch(/2026/);
});
