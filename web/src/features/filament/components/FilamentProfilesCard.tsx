import { StatusBadge } from '@/components/status-badge/StatusBadge';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import type { ProfileSource } from '@/features/profiles/data/mockProfiles';
import { profilesStore } from '@/features/profiles/data/mockProfilesStore';

const NOZZLE_SIZES = ['0.4', '0.6'] as const;
type NozzleSize = (typeof NOZZLE_SIZES)[number];

const SOURCE_BADGE_KIND: Record<ProfileSource, 'green' | 'blue' | 'zinc'> = {
  'Bambu Studio': 'green',
  OrcaSlicer: 'blue',
  PrusaSlicer: 'zinc',
};

interface FilamentProfilesCardProps {
  profilesByNozzleSize: Partial<Record<NozzleSize, string>>;
  onChange: (nozzleSize: NozzleSize, profileId: string) => void;
}

export function FilamentProfilesCard({
  profilesByNozzleSize,
  onChange,
}: FilamentProfilesCardProps) {
  const allProfiles = profilesStore.getSnapshot();

  return (
    <Card>
      <CardHeader>
        <CardTitle>Filament profiles</CardTitle>
        <CardDescription>
          Default slicer profile used for this filament at each nozzle size.
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {NOZZLE_SIZES.map((nozzleSize) => {
          const matchingProfiles = allProfiles.filter(
            (profile) =>
              profile.category === 'Filament' &&
              String(profile.nozzleSizeMm) === nozzleSize,
          );
          const selectedId = profilesByNozzleSize[nozzleSize];
          const selectedProfile = matchingProfiles.find(
            (profile) => profile.id === selectedId,
          );

          return (
            <div key={nozzleSize} className="flex flex-col gap-1.5">
              <span className="text-sm font-medium">{nozzleSize}mm nozzle</span>
              <Select
                value={selectedId}
                onValueChange={(value) => onChange(nozzleSize, value)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue placeholder="Choose a profile" />
                </SelectTrigger>
                <SelectContent>
                  {matchingProfiles.map((profile) => (
                    <SelectItem key={profile.id} value={profile.id}>
                      {profile.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {selectedProfile && (
                <div className="flex items-center gap-2 text-xs text-muted-foreground">
                  <span>
                    {selectedProfile.nozzleTempC}°C · bed{' '}
                    {selectedProfile.bedTempC}°C ·{' '}
                    {selectedProfile.maxVolumetricSpeedMm3s} mm³/s · fan{' '}
                    {selectedProfile.fanPercent}%
                  </span>
                  <StatusBadge
                    kind={SOURCE_BADGE_KIND[selectedProfile.source]}
                    label={selectedProfile.source}
                  />
                </div>
              )}
            </div>
          );
        })}
      </CardContent>
    </Card>
  );
}
