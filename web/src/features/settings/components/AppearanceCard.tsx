import { SidebarLayoutToggle } from '@/components/sidebar-layout-toggle/SidebarLayoutToggle';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import type { Density } from '@/lib/preferences/types';
import { usePreferences } from '@/lib/preferences/usePreferences';

export function AppearanceCard() {
  const {
    preferences: { density },
    setDensity,
  } = usePreferences();

  return (
    <Card>
      <CardHeader>
        <CardTitle>Appearance</CardTitle>
        <CardDescription>
          Sidebar layout and table density — saved to this browser.
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-5">
        <div className="flex items-center justify-between gap-4">
          <div>
            <p className="text-sm font-medium">Sidebar layout</p>
            <p className="text-xs text-muted-foreground">
              Flat, grouped, or rail (icon-only) navigation.
            </p>
          </div>
          <SidebarLayoutToggle />
        </div>
        <div className="flex items-center justify-between gap-4">
          <div>
            <p className="text-sm font-medium">Density</p>
            <p className="text-xs text-muted-foreground">
              Comfortable or compact spacing across tables and lists.
            </p>
          </div>
          <ToggleGroup
            type="single"
            variant="outline"
            value={density}
            onValueChange={(next) => {
              if (next) setDensity(next as Density);
            }}
          >
            <ToggleGroupItem value="comfortable">Comfortable</ToggleGroupItem>
            <ToggleGroupItem value="compact">Compact</ToggleGroupItem>
          </ToggleGroup>
        </div>
      </CardContent>
    </Card>
  );
}
