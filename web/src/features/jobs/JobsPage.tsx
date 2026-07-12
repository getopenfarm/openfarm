import { ClipboardListIcon } from 'lucide-react';
import { ComingSoonPage } from '@/components/coming-soon-page';

export function JobsPage() {
  return (
    <ComingSoonPage
      title="Jobs"
      description="The print queue — sending sliced files to printers and tracking job status — lands here."
      phase={2}
      icon={ClipboardListIcon}
    />
  );
}
