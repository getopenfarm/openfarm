/** WCAG 1.4.11 non-text contrast: white is readable on the fill up to a relative luminance of 1.05 / 3 - 0.05. */
const MAX_LUMINANCE_FOR_WHITE = 0.3;

/** White on any fill it still contrasts with (3:1), near-black only on light fills; max-ratio picking chose black on red. */
export function filamentIndicator(color?: string) {
  if (!color || !/^#[\da-f]{6}$/i.test(color)) {
    return { backgroundColor: 'var(--foreground)' };
  }
  const [red, green, blue] = [1, 3, 5].map((offset) => {
    const channel = Number.parseInt(color.slice(offset, offset + 2), 16) / 255;
    return channel <= 0.04045
      ? channel / 12.92
      : ((channel + 0.055) / 1.055) ** 2.4;
  });
  const luminance = red * 0.2126 + green * 0.7152 + blue * 0.0722;
  const useWhite = luminance <= MAX_LUMINANCE_FOR_WHITE;
  return { backgroundColor: useWhite ? '#ffffff' : '#171717' };
}
