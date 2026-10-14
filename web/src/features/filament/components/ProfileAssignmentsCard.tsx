import { ErrorState } from '@/components/empty-state/EmptyState';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { ProfilePickerCombobox } from '@/features/profiles/components/ProfilePickerCombobox';
import type {
  FilamentAssignmentFleet,
  FilamentProfileAssignmentInput,
  FilamentProfileAssignmentIssue,
  FilamentVariantResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsFilamentsGetFilamentAssignmentFleetEndpoint } from '@/lib/api/generated/filaments/filaments';

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
          <>
            <Skeleton className="h-32 w-full" />
            <Skeleton className="h-32 w-full" />
          </>
        )}
        {fleetQuery.isError && (
          <ErrorState
            className="col-span-full"
            size="sm"
            title="Could not load fleet variants"
            onRetry={() => fleetQuery.refetch()}
          />
        )}
        {fleet && rows.length === 0 && (
          <p className="col-span-full text-sm text-muted-foreground">
            Connect a printer to report its nozzle size. You can save this
            filament now and assign profiles later.
          </p>
        )}
        {!!fleet?.printersAwaitingNozzle?.length && (
          <p className="col-span-full text-sm text-status-attention">
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
