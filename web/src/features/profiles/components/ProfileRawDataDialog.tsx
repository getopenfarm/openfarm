import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';

interface ProfileRawDataDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  name?: string;
  rawDataJson?: string;
  isLoading?: boolean;
}

function formatRawDataJson(rawDataJson: string): string {
  try {
    return JSON.stringify(JSON.parse(rawDataJson), null, 2);
  } catch {
    // Should never happen (data-model.md guarantees well-formed JSON), but shown as-is rather
    // than crashing the dialog if it somehow does.
    return rawDataJson;
  }
}

// View-only for now (FR-011) — edit/save is added in US4.
export function ProfileRawDataDialog({
  open,
  onOpenChange,
  name,
  rawDataJson,
  isLoading,
}: ProfileRawDataDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[85vh] max-w-2xl overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{name ?? 'Profile'}</DialogTitle>
        </DialogHeader>
        {isLoading && <p className="text-sm text-muted-foreground">Loading…</p>}
        {!isLoading && rawDataJson && (
          <pre className="max-h-96 overflow-auto rounded-md bg-muted p-3 text-xs">
            {formatRawDataJson(rawDataJson)}
          </pre>
        )}
      </DialogContent>
    </Dialog>
  );
}
