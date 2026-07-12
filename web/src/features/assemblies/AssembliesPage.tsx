import { ComponentIcon } from 'lucide-react';
import { ComingSoonPage } from '@/components/coming-soon-page';

export function AssembliesPage() {
  return (
    <ComingSoonPage
      title="Assemblies"
      description="Bill-of-materials decomposition for multi-part builds lands here."
      phase={7}
      icon={ComponentIcon}
    />
  );
}
