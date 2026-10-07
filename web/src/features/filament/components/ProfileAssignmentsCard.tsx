import { useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { ProfilePickerCombobox } from '@/features/profiles/components/ProfilePickerCombobox';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import type {
  FilamentAssignmentFleet,
  FilamentProfileAssignmentInput,
  FilamentProfileAssignmentIssue,
  FilamentProfileAssignmentsResponse,
  FilamentVariantResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  useOpenFarmApiEndpointsFilamentsGetFilamentAssignmentFleetEndpoint,
  useOpenFarmApiEndpointsFilamentsGetFilamentProfileAssignmentsEndpoint,
  useOpenFarmApiEndpointsFilamentsUpdateFilamentProfileAssignmentsEndpoint,
} from '@/lib/api/generated/filaments/filaments';

interface Props {
  materialType: string;
  variants: Pick<FilamentVariantResponse, 'colorName' | 'colorHex'>[];
  assignments: FilamentProfileAssignmentInput[];
  issues?: FilamentProfileAssignmentIssue[];
  onChange: (value: FilamentProfileAssignmentInput[]) => void;
}

export function ProfileAssignmentsCard({
  materialType,
  variants,
  assignments,
  issues = [],
  onChange,
}: Props) {
  const fleetQuery =
    useOpenFarmApiEndpointsFilamentsGetFilamentAssignmentFleetEndpoint();
  const fleet = fleetQuery.data?.data as FilamentAssignmentFleet | undefined;
  const rows = [...(fleet?.variants ?? [])];
  for (const assignment of assignments) {
    if (
      !rows.some(
        (row) =>
          row.brand === assignment.brand &&
          row.modelCode === assignment.modelCode &&
          row.nozzleDiameterMm === assignment.nozzleDiameterMm,
      )
    ) {
      rows.push({
        brand: assignment.brand,
        modelCode: assignment.modelCode,
        nozzleDiameterMm: assignment.nozzleDiameterMm,
        printerCount: 0,
      });
    }
  }
  function setProfile(
    row: (typeof rows)[number],
    colorHex: string | null,
    profileId: string | null,
  ) {
    const retained = assignments.filter(
      (a) =>
        !(
          a.brand === row.brand &&
          a.modelCode === row.modelCode &&
          a.nozzleDiameterMm === row.nozzleDiameterMm &&
          (a.colorHex ?? null) === colorHex
        ),
    );
    onChange(
      profileId
        ? [
            ...retained,
            {
              brand: row.brand,
              modelCode: row.modelCode,
              nozzleDiameterMm: row.nozzleDiameterMm,
              filamentProfileId: profileId,
              colorHex,
            },
          ]
        : retained,
    );
  }
  return (
    <Card>
      <CardHeader>
        <CardTitle>Profile assignments</CardTitle>
        <p className="text-sm text-muted-foreground">
          Assign profiles for each printer model and reported nozzle. Required
          before production use. Colours inherit the default unless overridden.
          Compatible profiles from the default slicer appear first. Bambu Studio
          presets used in OrcaSlicer are validated before slicing.
        </p>
      </CardHeader>
      <CardContent className="grid grid-cols-1 items-start gap-5 md:grid-cols-2">
        {fleetQuery.isLoading && (
          <p className="col-span-full text-sm text-muted-foreground">
            Loading fleet variants…
          </p>
        )}
        {fleetQuery.isError && (
          <div className="col-span-full text-sm text-destructive">
            Could not load fleet variants.{' '}
            <Button
              type="button"
              variant="outline"
              onClick={() => fleetQuery.refetch()}
            >
              Retry
            </Button>
          </div>
        )}
        {fleet && rows.length === 0 && (
          <p className="col-span-full text-sm text-muted-foreground">
            Connect a printer to report its nozzle size. You can save this
            filament now and assign profiles later.
          </p>
        )}
        {!!fleet?.printersAwaitingNozzle?.length && (
          <p className="col-span-full text-sm text-amber-600">
            Waiting for nozzle information:{' '}
            {fleet.printersAwaitingNozzle.join(', ')}.
          </p>
        )}
        {rows.map((row) => {
          const base = assignments.find(
            (a) =>
              a.brand === row.brand &&
              a.modelCode === row.modelCode &&
              a.nozzleDiameterMm === row.nozzleDiameterMm &&
              !a.colorHex,
          );
          const rowIssues = issues
            .filter((issue) =>
              assignments.some(
                (a) =>
                  a.filamentProfileId === issue.assignment?.filamentProfileId &&
                  a.brand === issue.assignment?.brand &&
                  a.modelCode === issue.assignment?.modelCode &&
                  a.nozzleDiameterMm === issue.assignment?.nozzleDiameterMm &&
                  (a.colorHex ?? null) === (issue.assignment?.colorHex ?? null),
              ),
            )
            .filter(
              (issue) =>
                issue.assignment?.brand === row.brand &&
                issue.assignment?.modelCode === row.modelCode &&
                issue.assignment?.nozzleDiameterMm === row.nozzleDiameterMm,
            );
          const label = `${row.modelCode} · ${row.nozzleDiameterMm} mm`;
          return (
            <div
              key={`${row.brand}-${row.modelCode}-${row.nozzleDiameterMm}`}
              className="min-w-0 rounded-lg border p-4 flex flex-col gap-3"
            >
              <div className="flex justify-between gap-3">
                <span className="font-medium">{label}</span>
                <span className="text-sm text-muted-foreground">
                  {row.printerCount
                    ? `${row.printerCount} printer${row.printerCount === 1 ? '' : 's'}`
                    : 'Not currently in fleet'}{' '}
                  ·{' '}
                  {rowIssues.length
                    ? 'Invalid assignment'
                    : base
                      ? 'Assigned'
                      : 'Missing default'}
                </span>
              </div>
              {rowIssues.map((issue) => (
                <p
                  key={`${issue.assignment?.filamentProfileId}-${issue.assignment?.colorHex}`}
                  className="text-sm text-destructive"
                >
                  {issue.assignment?.colorHex
                    ? `${variants.find((v) => v.colorHex === issue.assignment?.colorHex)?.colorName ?? 'Colour'}: `
                    : 'Default: '}
                  {issue.message}
                </p>
              ))}
              <ProfilePickerCombobox
                kind="filament"
                ariaLabel={`Default profile for ${label}`}
                value={base?.filamentProfileId}
                onChange={(id) => setProfile(row, null, id)}
                materialType={materialType}
                brand={row.brand}
                modelCode={row.modelCode}
                nozzleDiameterMm={row.nozzleDiameterMm}
                placeholder="Assign filament profile"
              />
              {variants.length > 0 && (
                <details>
                  <summary className="cursor-pointer text-sm">
                    Colour overrides
                  </summary>
                  <div className="mt-3 flex flex-col gap-3">
                    {variants.map((variant) => {
                      const override = assignments.find(
                        (a) =>
                          a.brand === row.brand &&
                          a.modelCode === row.modelCode &&
                          a.nozzleDiameterMm === row.nozzleDiameterMm &&
                          a.colorHex === variant.colorHex,
                      );
                      return (
                        <div
                          key={variant.colorHex}
                          className="grid gap-2 sm:grid-cols-2"
                        >
                          <span className="text-sm self-center">
                            {variant.colorName} ·{' '}
                            {override
                              ? 'Override'
                              : base
                                ? 'Inherited'
                                : 'Missing assignment'}
                          </span>
                          <ProfilePickerCombobox
                            kind="filament"
                            ariaLabel={`${variant.colorName} profile for ${label}`}
                            value={override?.filamentProfileId}
                            onChange={(id) =>
                              setProfile(row, variant.colorHex ?? null, id)
                            }
                            materialType={materialType}
                            brand={row.brand}
                            modelCode={row.modelCode}
                            nozzleDiameterMm={row.nozzleDiameterMm}
                            placeholder={
                              base
                                ? 'Use filament default'
                                : 'No default assigned'
                            }
                          />
                        </div>
                      );
                    })}
                  </div>
                </details>
              )}
            </div>
          );
        })}
      </CardContent>
    </Card>
  );
}

export function EditProfileAssignments({
  filamentId,
  materialType,
  variants,
}: {
  filamentId: string;
  materialType: string;
  variants: FilamentVariantResponse[];
}) {
  const queryClient = useQueryClient();
  const query =
    useOpenFarmApiEndpointsFilamentsGetFilamentProfileAssignmentsEndpoint(
      filamentId,
    );
  const form = useForm<{ assignments: FilamentProfileAssignmentInput[] }>({
    defaultValues: { assignments: [] },
  });
  const { reset } = form;
  const isDirty = form.formState.isDirty;
  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(
    form.formState.isDirty,
  );
  useEffect(() => {
    if (query.data && !isDirty)
      reset({
        assignments:
          (query.data.data as FilamentProfileAssignmentsResponse).assignments ??
          [],
      });
  }, [query.data, reset, isDirty]);
  const mutation =
    useOpenFarmApiEndpointsFilamentsUpdateFilamentProfileAssignmentsEndpoint({
      mutation: {
        onSuccess: (response) => {
          form.reset({
            assignments:
              (response.data as FilamentProfileAssignmentsResponse)
                .assignments ?? [],
          });
          markClean();
          queryClient.invalidateQueries({
            predicate: (query) =>
              query.queryKey.some(
                (key) =>
                  typeof key === 'string' &&
                  /filaments|print-jobs|activity/.test(key),
              ),
          });
          toast.success('Profile assignments saved.');
        },
        onError: (error: unknown) =>
          toast.error(
            error instanceof Error
              ? error.message
              : 'Could not save profile assignments.',
          ),
      },
    });
  if (query.isLoading)
    return (
      <p className="text-sm text-muted-foreground">
        Loading profile assignments…
      </p>
    );
  if (query.isError)
    return (
      <p className="text-sm text-destructive">
        Could not load profile assignments.{' '}
        <Button variant="outline" onClick={() => query.refetch()}>
          Retry
        </Button>
      </p>
    );
  return (
    <form
      className="flex flex-col gap-3"
      onSubmit={form.handleSubmit((values) =>
        mutation.mutate({ id: filamentId, data: values }),
      )}
    >
      <ProfileAssignmentsCard
        materialType={materialType}
        variants={variants}
        assignments={form.watch('assignments')}
        issues={
          (query.data?.data as FilamentProfileAssignmentsResponse | undefined)
            ?.issues
        }
        onChange={(value) =>
          form.setValue('assignments', value, { shouldDirty: true })
        }
      />
      <Button
        className="self-end"
        type="submit"
        disabled={mutation.isPending || !form.formState.isDirty}
      >
        {mutation.isPending ? 'Saving…' : 'Save profile assignments'}
      </Button>
      <ConfirmLeaveDialog />
    </form>
  );
}
