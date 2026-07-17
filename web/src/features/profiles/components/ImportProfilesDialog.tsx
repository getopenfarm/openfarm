import { useQueryClient } from '@tanstack/react-query';
import { Upload } from 'lucide-react';
import type { ChangeEvent, DragEvent } from 'react';
import { useRef, useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog';
import type { PreviewProfileImportResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsProfilesListFilamentProfilesEndpointQueryKey,
  getOpenFarmApiEndpointsProfilesListMachineProfilesEndpointQueryKey,
  useOpenFarmApiEndpointsProfilesConfirmProfileImportEndpoint,
  useOpenFarmApiEndpointsProfilesPreviewProfileImportEndpoint,
} from '@/lib/api/generated/profiles/profiles';
import { cn } from '@/lib/utils';
import {
  ProfileReviewList,
  type ProfileSelectionMap,
  type ProfileSelectionState,
  profileSelectionKey,
} from './ProfileReviewList';

const ACCEPTED_EXTENSIONS =
  '.zip,.orca_printer,.orca_filament,.bbscfg,.bbsflmt';

function splitSelectionKey(key: string): {
  type: 'Machine' | 'Filament';
  name: string;
} {
  const separatorIndex = key.indexOf(':');
  return {
    type: key.slice(0, separatorIndex) as 'Machine' | 'Filament',
    name: key.slice(separatorIndex + 1),
  };
}

export function ImportProfilesDialog() {
  const [open, setOpen] = useState(false);
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<PreviewProfileImportResponse | null>(
    null,
  );
  const [selections, setSelections] = useState<ProfileSelectionMap>({});
  const [isDragging, setIsDragging] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const queryClient = useQueryClient();

  const previewMutation =
    useOpenFarmApiEndpointsProfilesPreviewProfileImportEndpoint({
      mutation: {
        onSuccess: (response) => {
          const data = response.data as PreviewProfileImportResponse;
          setPreview(data);

          const initial: ProfileSelectionMap = {};
          for (const preset of data.machinePresets ?? []) {
            if (preset.name) {
              initial[profileSelectionKey('Machine', preset.name)] = {
                selected: true,
              };
            }
          }
          for (const preset of data.filamentPresets ?? []) {
            if (preset.name) {
              initial[profileSelectionKey('Filament', preset.name)] = {
                selected: true,
              };
            }
          }
          setSelections(initial);
        },
        onError: () => {
          toast.error(
            'Could not read this file as an OrcaSlicer or Bambu Studio profile bundle.',
          );
          setFile(null);
        },
      },
    });

  const confirmMutation =
    useOpenFarmApiEndpointsProfilesConfirmProfileImportEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Profiles imported.');
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsProfilesListMachineProfilesEndpointQueryKey(),
          });
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsProfilesListFilamentProfilesEndpointQueryKey(),
          });
          resetAndClose();
        },
        onError: () => toast.error('Could not import the selected profiles.'),
      },
    });

  function resetAndClose() {
    setOpen(false);
    setFile(null);
    setPreview(null);
    setSelections({});
  }

  function processFile(selectedFile: File) {
    setFile(selectedFile);
    setPreview(null);
    previewMutation.mutate({ data: { file: selectedFile } });
  }

  function handleFileChange(event: ChangeEvent<HTMLInputElement>) {
    const selectedFile = event.target.files?.[0];
    // Reset so picking the same file again still fires a change event.
    event.target.value = '';
    if (selectedFile) {
      processFile(selectedFile);
    }
  }

  function openFileDialog() {
    fileInputRef.current?.click();
  }

  function handleDragOver(event: DragEvent<HTMLButtonElement>) {
    // Required so the browser allows a drop here instead of rejecting it.
    event.preventDefault();
    if (!previewMutation.isPending) {
      setIsDragging(true);
    }
  }

  function handleDragLeave(event: DragEvent<HTMLButtonElement>) {
    event.preventDefault();
    setIsDragging(false);
  }

  function handleDrop(event: DragEvent<HTMLButtonElement>) {
    event.preventDefault();
    setIsDragging(false);
    if (previewMutation.isPending) {
      return;
    }

    const droppedFile = event.dataTransfer.files?.[0];
    if (droppedFile) {
      processFile(droppedFile);
    }
  }

  function handleSelectionChange(key: string, next: ProfileSelectionState) {
    setSelections((prev) => ({ ...prev, [key]: next }));
  }

  function handleConfirm() {
    if (!file) {
      return;
    }

    // Re-uploads the same file rather than trusting anything echoed from the preview response
    // (research.md §4) — the server re-parses it itself.
    const selectionPayload = Object.entries(selections)
      .filter(([, state]) => state.selected)
      .map(([key, state]) => {
        const { type, name } = splitSelectionKey(key);
        return { type, name, duplicateAction: state.duplicateAction ?? null };
      });

    confirmMutation.mutate({
      data: {
        file,
        selections: JSON.stringify(selectionPayload),
      },
    });
  }

  const hasPreview = preview !== null;
  const machinePresets = preview?.machinePresets ?? [];
  const filamentPresets = preview?.filamentPresets ?? [];
  const nothingImportable =
    hasPreview && machinePresets.length === 0 && filamentPresets.length === 0;
  const nothingSelected = Object.values(selections).every(
    (state) => !state.selected,
  );

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (next) {
          setOpen(true);
        } else {
          resetAndClose();
        }
      }}
    >
      <DialogTrigger asChild>
        <Button>Import</Button>
      </DialogTrigger>
      <DialogContent className="flex max-h-[85vh] max-w-2xl flex-col gap-0 overflow-hidden p-0 sm:max-w-3xl">
        <DialogHeader className="shrink-0 gap-2 border-b p-4">
          <DialogTitle>Import profiles</DialogTitle>
          <DialogDescription>
            Upload an OrcaSlicer or Bambu Studio profile bundle, then choose
            which presets to keep. Nothing is saved until you confirm.
          </DialogDescription>
        </DialogHeader>

        <div className="flex-1 overflow-y-auto p-4">
          {!hasPreview && (
            <div className="flex flex-col gap-2">
              <button
                type="button"
                aria-label="Choose a profile bundle file, or drag and drop it here"
                disabled={previewMutation.isPending}
                onClick={openFileDialog}
                onDragOver={handleDragOver}
                onDragEnter={handleDragOver}
                onDragLeave={handleDragLeave}
                onDrop={handleDrop}
                className={cn(
                  'flex cursor-pointer flex-col items-center justify-center gap-2 rounded-lg border-2 border-dashed p-8 text-center transition-colors',
                  isDragging
                    ? 'border-primary bg-primary/5'
                    : 'border-input hover:border-primary/50',
                  previewMutation.isPending &&
                    'pointer-events-none cursor-default opacity-60',
                )}
              >
                <Upload className="size-6 text-muted-foreground" />
                <p className="text-sm">
                  <span className="font-medium text-primary">
                    Click to browse
                  </span>{' '}
                  or drag and drop a profile bundle
                </p>
                <p className="text-xs text-muted-foreground">
                  {ACCEPTED_EXTENSIONS.split(',').join(', ')}
                </p>
              </button>
              <input
                ref={fileInputRef}
                type="file"
                accept={ACCEPTED_EXTENSIONS}
                onChange={handleFileChange}
                disabled={previewMutation.isPending}
                className="hidden"
              />
            </div>
          )}

          {nothingImportable && (
            <p className="text-sm text-muted-foreground">
              Nothing importable was found in this bundle.
            </p>
          )}

          {hasPreview && !nothingImportable && (
            <ProfileReviewList
              machinePresets={machinePresets}
              filamentPresets={filamentPresets}
              selections={selections}
              onChange={handleSelectionChange}
            />
          )}
        </div>

        {hasPreview && !nothingImportable && (
          <DialogFooter className="mx-0 mb-0 shrink-0">
            <Button
              onClick={handleConfirm}
              disabled={confirmMutation.isPending || nothingSelected}
            >
              {confirmMutation.isPending ? 'Importing…' : 'Confirm import'}
            </Button>
          </DialogFooter>
        )}
      </DialogContent>
    </Dialog>
  );
}
