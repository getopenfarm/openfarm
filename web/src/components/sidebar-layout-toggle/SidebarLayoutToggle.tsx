import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import type { SidebarVariant } from '@/lib/preferences/types';
import { usePreferences } from '@/lib/preferences/usePreferences';

const OPTIONS: Array<{ value: SidebarVariant; label: string }> = [
  { value: 'flat', label: 'Flat' },
  { value: 'grouped', label: 'Grouped' },
  { value: 'rail', label: 'Rail' },
];

export function SidebarLayoutToggle() {
  const {
    preferences: { sidebarVariant },
    setSidebarVariant,
  } = usePreferences();

  return (
    <ToggleGroup
      type="single"
      variant="outline"
      value={sidebarVariant}
      onValueChange={(next) => {
        if (next) setSidebarVariant(next as SidebarVariant);
      }}
    >
      {OPTIONS.map((option) => (
        <ToggleGroupItem key={option.value} value={option.value}>
          {option.label}
        </ToggleGroupItem>
      ))}
    </ToggleGroup>
  );
}
