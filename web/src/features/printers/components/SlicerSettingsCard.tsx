import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import type { PrinterDetailResponse } from '@/lib/api/generated/endpoints.schemas';

/** The slicer settings of a printer. The header's Slicer settings button edits them. */
export function SlicerSettingsCard({
  printer,
}: {
  printer: PrinterDetailResponse;
}) {
  return (
    <Card className="gap-0 py-0">
      <CardHeader className="border-b px-[18px] py-3.5">
        <CardTitle className="text-sm font-semibold">Slicer settings</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-2 px-[18px] py-3.5">
        <p className="text-sm">
          Build plate:{' '}
          <span className="text-muted-foreground">
            {printer.buildPlateTypeLabel ?? 'Not selected'}
          </span>
        </p>
        {printer.setupIssues?.length ? (
          <p className="text-sm text-status-attention">
            {printer.setupIssues.join(' · ')}
          </p>
        ) : null}
        <p className="text-xs text-muted-foreground">
          Files go only to a printer with their plate.
        </p>
      </CardContent>
    </Card>
  );
}
