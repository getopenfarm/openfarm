import { Rows2Icon, Rows3Icon, Rows4Icon } from 'lucide-react';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import type { DensityMode, ListPageKey } from '@/lib/preferences/types';
import { usePreferences } from '@/lib/preferences/usePreferences';

interface DensityToggleProps {
  page: ListPageKey;
}

const DENSITY_OPTIONS: {
  value: DensityMode;
  label: string;
  icon: typeof Rows2Icon;
}[] = [
  { value: 'small', label: 'Small', icon: Rows4Icon },
  { value: 'medium', label: 'Medium', icon: Rows3Icon },
  { value: 'large', label: 'Large', icon: Rows2Icon },
];

export function DensityToggle({ page }: DensityToggleProps) {
  const { getListDensity, setListDensity } = usePreferences();
  const density = getListDensity(page);

  return (
    <ToggleGroup
      type="single"
      variant="outline"
      value={density}
      onValueChange={(next) => {
        if (next === 'small' || next === 'medium' || next === 'large') {
          setListDensity(page, next);
        }
      }}
    >
      {DENSITY_OPTIONS.map(({ value, label, icon: Icon }) => (
        <ToggleGroupItem key={value} value={value} aria-label={`${label} size`}>
          <Icon />
        </ToggleGroupItem>
      ))}
    </ToggleGroup>
  );
}
