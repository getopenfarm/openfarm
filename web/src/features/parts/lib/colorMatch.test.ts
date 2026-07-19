import { describe, expect, it } from 'vitest';
import type { FilamentResponse } from '@/lib/api/generated/endpoints.schemas';
import { autoMatchColor, colorDistance, hexToRgb } from './colorMatch';

describe('colorMatch', () => {
  describe('hexToRgb', () => {
    it('converts a hex string to its rgb components', () => {
      expect(hexToRgb('#ff0000')).toEqual({ r: 255, g: 0, b: 0 });
      expect(hexToRgb('#18181b')).toEqual({ r: 24, g: 24, b: 27 });
    });
  });

  describe('colorDistance', () => {
    it('returns 0 for identical colors', () => {
      expect(colorDistance('#3b82f6', '#3b82f6')).toBe(0);
    });

    it('returns a larger distance for more different colors', () => {
      const near = colorDistance('#000000', '#101010');
      const far = colorDistance('#000000', '#ffffff');
      expect(far).toBeGreaterThan(near);
    });
  });

  describe('autoMatchColor', () => {
    it('returns an exact match with distance 0', () => {
      const filaments: FilamentResponse[] = [
        {
          id: 'f1',
          vendorName: 'Bambu Lab',
          material: 'PLA Basic',
          materialType: 'PLA',
          variants: [{ id: 'v1', colorName: 'Blue', colorHex: '#3b82f6' }],
        },
      ];

      const result = autoMatchColor('#3b82f6', 'PLA', filaments);

      expect(result).toEqual({
        filamentId: 'f1',
        variantId: 'v1',
        distance: 0,
      });
    });

    it('picks the closer of two candidates (nearest-neighbor)', () => {
      const filaments: FilamentResponse[] = [
        {
          id: 'f-black',
          vendorName: 'A',
          material: 'M1',
          materialType: 'PLA',
          variants: [
            { id: 'v-black', colorName: 'Black', colorHex: '#000000' },
          ],
        },
        {
          id: 'f-white',
          vendorName: 'B',
          material: 'M2',
          materialType: 'PLA',
          variants: [
            { id: 'v-white', colorName: 'White', colorHex: '#ffffff' },
          ],
        },
      ];

      // Much closer to black than white.
      const result = autoMatchColor('#101010', 'PLA', filaments);

      expect(result?.variantId).toBe('v-black');
    });

    it('returns null when no filaments exist for that material type', () => {
      const filaments: FilamentResponse[] = [
        {
          id: 'f-petg',
          vendorName: 'A',
          material: 'M1',
          materialType: 'PETG',
          variants: [{ id: 'v1', colorName: 'Gray', colorHex: '#71717a' }],
        },
      ];

      expect(autoMatchColor('#000000', 'PLA', filaments)).toBeNull();
    });
  });
});
