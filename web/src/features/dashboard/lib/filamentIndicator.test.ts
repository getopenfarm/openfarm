import { describe, expect, it } from 'vitest';
import { filamentIndicator } from './filamentIndicator';

describe('Filament feeding indicator contrast', () => {
  it.each(['#000000', '#0000ff', '#800000', '#777777'])(
    'uses white on darker filament %s',
    (color) => {
      expect(filamentIndicator(color).backgroundColor).toBe('#ffffff');
    },
  );
  it.each(['#ffffff', '#ffff00', '#00ff00', '#ff0000', '#888888'])(
    'uses near-black on lighter filament %s',
    (color) => {
      expect(filamentIndicator(color).backgroundColor).toBe('#171717');
    },
  );
  it.each([undefined, 'invalid'])(
    'uses theme-aware contrast when color is unavailable: %s',
    (color) => {
      expect(filamentIndicator(color).backgroundColor).toBe(
        'var(--foreground)',
      );
    },
  );
});
