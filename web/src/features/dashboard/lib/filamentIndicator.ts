/** Choose the higher-contrast neutral against the actual filament fill. */
export function filamentIndicator(color?: string) {
  if (!color || !/^#[\da-f]{6}$/i.test(color)) {
    return {
      backgroundColor: 'var(--foreground)',
      boxShadow: '0 0 0 1px color-mix(in srgb, var(--card) 50%, transparent)',
    };
  }
  const [red, green, blue] = [1, 3, 5].map((offset) => {
    const channel = Number.parseInt(color.slice(offset, offset + 2), 16) / 255;
    return channel <= 0.04045
      ? channel / 12.92
      : ((channel + 0.055) / 1.055) ** 2.4;
  });
  const luminance = red * 0.2126 + green * 0.7152 + blue * 0.0722;
  const useWhite =
    1.05 / (luminance + 0.05) >= (luminance + 0.05) / (0.0085681256 + 0.05);
  return {
    backgroundColor: useWhite ? '#ffffff' : '#171717',
    boxShadow: `0 0 0 1px ${useWhite ? 'rgb(23 23 23 / 45%)' : 'rgb(255 255 255 / 45%)'}`,
  };
}
