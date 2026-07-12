import { WrenchIcon } from 'lucide-react';
import { ComingSoonPage } from '@/components/coming-soon-page';

export function MaintenancePage() {
  return (
    <ComingSoonPage
      title="Maintenance"
      description="Threshold-based maintenance tracking and due-date alerts land here."
      phase={10}
      icon={WrenchIcon}
    />
  );
}
