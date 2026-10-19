import { describe, expect, it } from 'vitest';
import { inferMaterialType } from './materialType';

describe('inferMaterialType', () => {
  it.each([
    ['PETG Basic', 'PETG'],
    ['PLA Pro', 'PLA'],
    ['PLA Matte', 'PLA'],
    ['Silk pla', 'PLA'],
    ['PLA+', 'PLA'],
    ['PETG-CF', 'PETG'],
    ['PETG GF', 'PETG'],
    ['ABS-GF', 'ABS'],
    ['TPU 95A', 'TPU'],
    ['PC FR', 'PC'],
  ])('reads %s as %s', (name, type) => {
    expect(inferMaterialType(name)).toBe(type);
  });

  it.each([
    ['', 'empty'],
    ['Basic', 'no type'],
    ['PCTG', 'type only as part of a word'],
    ['PA6-CF', 'unknown type'],
    ['PC-ABS', 'two types'],
  ])('returns null for %j (%s)', (name) => {
    expect(inferMaterialType(name)).toBeNull();
  });
});
