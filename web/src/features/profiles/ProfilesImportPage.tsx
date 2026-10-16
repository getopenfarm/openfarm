import { useQueryClient } from '@tanstack/react-query';
import { PackageOpenIcon, Upload } from 'lucide-react';
import type { ChangeEvent, DragEvent } from 'react';
import { useRef, useState } from 'react';
import { useNavigate } from 'react-router';
import { toast } from 'sonner';
import { BackLink } from '@/components/back-link/BackLink';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { Spinner } from '@/components/ui/spinner';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
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
} from './components/ProfileReviewList';

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

export function ProfilesImportPage() {
  const navigate = useNavigate();
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<PreviewProfileImportResponse | null>(
    null,
  );
  const [selections, setSelections] = useState<ProfileSelectionMap>({});
  const [isDragging, setIsDragging] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const queryClient = useQueryClient();

  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(
    file !== null,
  );

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
            'Could not read file. Use an OrcaSlicer or Bambu Studio bundle.',
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
          markClean();
          navigate('/profiles');
        },
        onError: () => toast.error('Could not import profiles.'),
      },
    });

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
    <Page className="flex-1">
      <PageHeader
        above={
          <BackLink to="/profiles" className="mb-1">
            Back to profiles
          </BackLink>
        }
        title="Import profiles"
        description="Upload an OrcaSlicer or Bambu Studio bundle. Nothing is saved until you confirm."
      />

      <Card>
        <CardContent className="flex flex-col gap-4">
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
            <EmptyState
              size="sm"
              icon={
                <PackageOpenIcon className="size-8 text-muted-foreground" />
              }
              title="Nothing to import"
              description="No profiles in this bundle."
              action={
                <Button
                  variant="outline"
                  onClick={() => {
                    setFile(null);
                    setPreview(null);
                  }}
                >
                  Choose another file
                </Button>
              }
            />
          )}

          {hasPreview && !nothingImportable && (
            <ProfileReviewList
              machinePresets={machinePresets}
              filamentPresets={filamentPresets}
              selections={selections}
              onChange={handleSelectionChange}
            />
          )}
        </CardContent>
      </Card>

      <FormFooter
        end={
          <>
            <Button
              type="button"
              variant="outline"
              onClick={() => navigate('/profiles')}
            >
              Cancel
            </Button>
            {hasPreview && !nothingImportable ? (
              <Button
                onClick={handleConfirm}
                disabled={confirmMutation.isPending || nothingSelected}
              >
                {confirmMutation.isPending && (
                  <Spinner data-icon="inline-start" />
                )}
                {confirmMutation.isPending ? 'Importing…' : 'Import'}
              </Button>
            ) : undefined}
          </>
        }
      />

      <ConfirmLeaveDialog />
    </Page>
  );
}
