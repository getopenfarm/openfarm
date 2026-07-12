import { PrinterIcon } from 'lucide-react';
import { ComingSoonPage } from '@/components/coming-soon-page';

export function PrintersPage() {
  return (
    <ComingSoonPage
      title="Printers"
      description="Fleet management and live Bambu integration — status, cameras, and control — land here."
      phase={1}
      icon={PrinterIcon}
    />
  );
}
