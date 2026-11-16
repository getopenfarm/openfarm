import {
  DownloadIcon,
  MoreHorizontalIcon,
  RefreshCwIcon,
  UploadIcon,
} from 'lucide-react';
import { useRef, useState } from 'react';
import { PermissionButton as Button } from '@/components/PermissionButton';
import { PermissionMenuItem } from '@/components/PermissionMenuItem';
import { CardAction } from '@/components/ui/card';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Spinner } from '@/components/ui/spinner';
import { downloadFile } from '@/lib/api/downloadFile';
import type { PartResponse } from '@/lib/api/generated/endpoints.schemas';
import { useAllowed } from '@/lib/auth/AccessContext';
import { AddOrReplacePartDialog } from './AddOrReplacePartDialog';

export function PartFileActions({ part }: { part: PartResponse }) {
  const canDownload = useAllowed('parts:read');
  const canReplace = useAllowed('parts:replace-file');
  const triggerRef = useRef<HTMLButtonElement>(null);
  const downloadingRef = useRef(false);
  const [downloading, setDownloading] = useState(false);
  const [mode, setMode] = useState<'replace' | 'reprocess'>();

  async function download() {
    if (!part.id || downloadingRef.current) return;
    downloadingRef.current = true;
    setDownloading(true);
    try {
      await downloadFile(
        `/api/v1/parts/${part.id}/file`,
        part.originalFileName ?? 'part',
        'Could not download part file.',
      );
    } finally {
      downloadingRef.current = false;
      setDownloading(false);
    }
  }

  return (
    <>
      {(canDownload || canReplace) && (
        <CardAction className="-my-1.5 row-span-1 self-center">
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button
                ref={triggerRef}
                type="button"
                variant="ghost"
                size="icon-sm"
                aria-label="File actions"
                aria-busy={downloading}
                disabled={!part.id}
              >
                {downloading ? (
                  <Spinner aria-hidden="true" />
                ) : (
                  <MoreHorizontalIcon />
                )}
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="min-w-40">
              <DropdownMenuGroup>
                <PermissionMenuItem
                  permission="parts:read"
                  disabled={downloading}
                  title="Downloads the original uploaded file, including all plates."
                  onSelect={() => void download()}
                >
                  {downloading ? (
                    <Spinner aria-hidden="true" />
                  ) : (
                    <DownloadIcon />
                  )}
                  {downloading ? 'Downloading…' : 'Download file'}
                </PermissionMenuItem>
                <PermissionMenuItem
                  permission="parts:replace-file"
                  onSelect={() => setMode('replace')}
                >
                  <UploadIcon /> Replace file
                </PermissionMenuItem>
                <PermissionMenuItem
                  permission="parts:replace-file"
                  onSelect={() => setMode('reprocess')}
                >
                  <RefreshCwIcon /> Reprocess
                </PermissionMenuItem>
              </DropdownMenuGroup>
            </DropdownMenuContent>
          </DropdownMenu>
        </CardAction>
      )}
      <AddOrReplacePartDialog
        partId={part.id}
        partName={part.name}
        currentSlicer={part.slicer}
        currentPlateNumber={part.plateNumber}
        currentPlateName={part.plateName}
        mode={mode ?? 'replace'}
        open={mode !== undefined}
        onOpenChange={(open) => {
          if (!open) setMode(undefined);
        }}
        returnFocusRef={triggerRef}
      />
    </>
  );
}
