import { useQueryClient } from '@tanstack/react-query';
import type { ChangeEvent } from 'react';
import { useState } from 'react';
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
import {
  ProfileReviewList,
  type ProfileSelectionMap,
  type ProfileSelectionState,
  profileSelectionKey,
} from './ProfileReviewList';

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

  function handleFileChange(event: ChangeEvent<HTMLInputElement>) {
    const selectedFile = event.target.files?.[0];
    if (!selectedFile) {
      return;
    }

    setFile(selectedFile);
    setPreview(null);
    previewMutation.mutate({ data: { file: selectedFile } });
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
      <DialogContent className="max-h-[85vh] max-w-2xl overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Import profiles</DialogTitle>
          <DialogDescription>
            Upload an OrcaSlicer or Bambu Studio profile bundle, then choose
            which presets to keep. Nothing is saved until you confirm.
          </DialogDescription>
        </DialogHeader>

        {!hasPreview && (
          <div className="flex flex-col gap-2">
            <input
              type="file"
              accept=".zip,.orca_printer,.orca_filament,.bbscfg,.bbsflmt"
              onChange={handleFileChange}
              disabled={previewMutation.isPending}
              className="text-sm"
            />
            {previewMutation.isPending && (
              <p className="text-sm text-muted-foreground">Reading bundle…</p>
            )}
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

        <DialogFooter>
          {hasPreview && !nothingImportable && (
            <Button
              onClick={handleConfirm}
              disabled={confirmMutation.isPending || nothingSelected}
            >
              {confirmMutation.isPending ? 'Importing…' : 'Confirm import'}
            </Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
