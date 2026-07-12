import { BoxIcon } from 'lucide-react';
import { ComingSoonPage } from '@/components/coming-soon-page';

export function PartsPage() {
  return (
    <ComingSoonPage
      title="Parts"
      description="The reusable part library that assemblies and BOMs are built from lands here."
      phase={6}
      icon={BoxIcon}
    />
  );
}
