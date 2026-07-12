import { LayersIcon } from 'lucide-react';
import { ComingSoonPage } from '@/components/coming-soon-page';

export function FilamentPage() {
  return (
    <ComingSoonPage
      title="Filament"
      description="Spool tracking and weight-based usage — knowing what's loaded and what's left — lands here."
      phase={3}
      icon={LayersIcon}
    />
  );
}
