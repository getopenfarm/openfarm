import { describe, expect, it } from 'vitest';
import {
  formatCelsius,
  formatDays,
  formatFileSize,
  formatGrams,
  formatMm,
  formatWeight,
} from './formatUnits';

describe('formatUnits', () => {
  it('formats grams', () => {
    expect(formatGrams(250)).toBe('250 g');
    expect(formatGrams(12.34)).toBe('12.3 g');
    expect(formatGrams(5.0)).toBe('5 g');
  });
  it('switches to kilograms from 1 kg', () => {
    expect(formatWeight(750)).toBe('750 g');
    expect(formatWeight(999.9)).toBe('999.9 g');
    expect(formatWeight(1000)).toBe('1 kg');
    expect(formatWeight(2500)).toBe('2.5 kg');
    expect(formatWeight(3333)).toBe('3.33 kg');
  });
  it('formats days left as an estimate, capped', () => {
    expect(formatDays(6.4)).toBe('~6 d');
    expect(formatDays(20.8)).toBe('~20 d');
    expect(formatDays(0.4)).toBe('< 1 d');
    expect(formatDays(60)).toBe('60+ d');
    expect(formatDays(400, 365)).toBe('365+ d');
  });
  it('formats mm and celsius', () => {
    expect(formatMm(0.4)).toBe('0.4 mm');
    expect(formatCelsius(215)).toBe('215 °C');
  });
  it('formats file size', () => {
    expect(formatFileSize(10)).toBe('1 KB');
    expect(formatFileSize(2048)).toBe('2 KB');
    expect(formatFileSize(1_572_864)).toBe('1.5 MB');
    expect(formatFileSize(0)).toBe('0 KB');
  });
});
