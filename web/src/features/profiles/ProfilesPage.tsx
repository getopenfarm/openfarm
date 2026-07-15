import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { ProfilesTable } from './components/ProfilesTable';
import { useProfiles } from './data/mockProfilesStore';

export function ProfilesPage() {
  const [profiles] = useProfiles();

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="font-heading text-2xl font-semibold">Profiles</h2>
          <p className="text-sm text-muted-foreground">
            {profiles.length} slicer profiles
          </p>
        </div>
        <Button onClick={() => toast('Not yet implemented')}>Import</Button>
      </div>
      <ProfilesTable profiles={profiles} />
    </div>
  );
}
