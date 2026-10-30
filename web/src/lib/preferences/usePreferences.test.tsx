import { act, renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PREFERENCES_STORAGE_KEY } from './defaultPreferences';
import { PreferencesProvider, usePreferences } from './usePreferences';

const setup = () =>
  renderHook(() => usePreferences(), { wrapper: PreferencesProvider });
beforeEach(() => localStorage.clear());

describe('library field preferences', () => {
  it('keeps library/view choices separate and restores them after remount', () => {
    const first = setup();
    expect(first.result.current.getLibraryFields('parts', 'table').tags).toBe(
      true,
    );
    expect(first.result.current.getLibraryFields('parts', 'grid')).toEqual({
      fileType: true,
      material: true,
      nozzleDiameter: false,
      estimate: false,
    });
    expect(first.result.current.getLibraryFields('assemblies', 'grid')).toEqual(
      { description: false, lineCount: false },
    );
    act(() => {
      first.result.current.setListView('parts', 'grid');
      first.result.current.setLibraryFields('parts', 'table', (prev) => ({
        ...prev,
        material: false,
      }));
      first.result.current.setLibraryFields('parts', 'grid', (prev) => ({
        ...prev,
        estimate: true,
      }));
      first.result.current.setLibraryFields('assemblies', 'grid', {
        lineCount: true,
      });
    });
    first.unmount();
    const restored = setup().result;
    expect(restored.current.getListView('parts')).toBe('grid');
    expect(restored.current.getLibraryFields('parts', 'table').material).toBe(
      false,
    );
    expect(restored.current.getLibraryFields('parts', 'grid').material).toBe(
      true,
    );
    expect(restored.current.getLibraryFields('parts', 'grid').estimate).toBe(
      true,
    );
    expect(
      restored.current.getLibraryFields('assemblies', 'grid').lineCount,
    ).toBe(true);
    expect(
      restored.current.getLibraryFields('assemblies', 'table').lineCount,
    ).toBe(true);
  });

  it('loads older preferences without losing view or density', () => {
    localStorage.setItem(
      PREFERENCES_STORAGE_KEY,
      JSON.stringify({
        listViews: { parts: 'grid' },
        listDensities: { parts: 'large' },
      }),
    );
    const { result } = setup();
    expect(result.current.getListView('parts')).toBe('grid');
    expect(result.current.getListDensity('parts')).toBe('large');
    expect(result.current.getLibraryFields('parts', 'grid').estimate).toBe(
      false,
    );
  });

  it.each([
    'not json',
    'null',
    JSON.stringify({
      libraryFields: {
        parts: {
          grid: {
            material: 'false',
            estimate: true,
            tags: true,
            name: false,
            extra: true,
          },
        },
      },
    }),
  ])('uses supported booleans with safe defaults for %s', (raw) => {
    localStorage.setItem(PREFERENCES_STORAGE_KEY, raw);
    const { result } = setup();
    const fields = result.current.getLibraryFields('parts', 'grid');
    expect(fields.material).toBe(true);
    expect(fields).not.toHaveProperty('tags');
    expect(fields).not.toHaveProperty('name');
    expect(fields).not.toHaveProperty('extra');
    expect(fields.estimate).toBe(raw.startsWith('{'));
  });

  it('keeps controls working when storage cannot be read or written', () => {
    const read = vi
      .spyOn(Storage.prototype, 'getItem')
      .mockImplementation(() => {
        throw new Error('blocked');
      });
    const write = vi
      .spyOn(Storage.prototype, 'setItem')
      .mockImplementation(() => {
        throw new Error('quota');
      });
    try {
      const { result } = setup();
      act(() =>
        result.current.setLibraryFields('parts', 'grid', { material: false }),
      );
      expect(result.current.getLibraryFields('parts', 'grid').material).toBe(
        false,
      );
    } finally {
      read.mockRestore();
      write.mockRestore();
    }
  });
});
