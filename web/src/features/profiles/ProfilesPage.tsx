import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { ImportProfilesDialog } from './components/ImportProfilesDialog';

export function ProfilesPage() {
  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="font-heading text-2xl font-semibold">Profiles</h2>
          <p className="text-sm text-muted-foreground">
            Machine and filament profiles imported from slicer bundles
          </p>
        </div>
        <ImportProfilesDialog />
      </div>
      <Tabs defaultValue="machine">
        <TabsList>
          <TabsTrigger value="machine">Machine</TabsTrigger>
          <TabsTrigger value="filament">Filament</TabsTrigger>
        </TabsList>
        <TabsContent value="machine">
          <p className="text-sm text-muted-foreground">
            No machine profiles yet. Import a slicer bundle to populate this
            catalog.
          </p>
        </TabsContent>
        <TabsContent value="filament">
          <p className="text-sm text-muted-foreground">
            No filament profiles yet. Import a slicer bundle to populate this
            catalog.
          </p>
        </TabsContent>
      </Tabs>
    </div>
  );
}
