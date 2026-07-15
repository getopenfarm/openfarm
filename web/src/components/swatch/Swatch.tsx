import { cn } from '@/lib/utils';

interface SwatchProps {
  colorHex?: string;
  auto?: boolean;
  size?: 'sm' | 'md' | 'lg';
  className?: string;
}

const SIZE_CLASS: Record<NonNullable<SwatchProps['size']>, string> = {
  sm: 'size-3',
  md: 'size-4',
  lg: 'size-5',
};

export function Swatch({
  colorHex,
  auto,
  size = 'md',
  className,
}: SwatchProps) {
  return (
    <span
      data-slot="swatch"
      className={cn(
        'inline-block shrink-0 rounded-[4px] ring-1 ring-inset ring-foreground/10',
        SIZE_CLASS[size],
        auto && 'swatch-rainbow',
        className,
      )}
      style={!auto && colorHex ? { backgroundColor: colorHex } : undefined}
    />
  );
}
