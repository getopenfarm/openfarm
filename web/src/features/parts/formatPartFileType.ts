export function formatPartFileType({
  fileFormat,
  requiresSlicing,
}: {
  fileFormat?: string;
  requiresSlicing?: boolean;
}): string {
  switch (fileFormat) {
    case 'ThreeMf':
      return requiresSlicing === true
        ? 'Plain 3MF'
        : requiresSlicing === false
          ? 'Sliced 3MF'
          : '3MF';
    case 'GCode':
      return 'G-code';
    default:
      return fileFormat || '—';
  }
}
