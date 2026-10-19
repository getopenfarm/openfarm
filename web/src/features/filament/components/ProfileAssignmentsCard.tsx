import { ErrorState } from '@/components/empty-state/EmptyState';
import { Badge } from '@/components/ui/badge';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { ProfilePickerCombobox } from '@/features/profiles/components/ProfilePickerCombobox';
import type {
  FilamentProfileAssignmentInput,
  FilamentProfileAssignmentIssue,
  FilamentVariantResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  type AssignmentRow,
  matchesRow,
  rowLabel,
  useAssignmentRows,
} from './useAssignmentRows';

interface Props {
  materialType: string;
  /** Names the color in issue messages for color overrides. */
  variants: Pick<FilamentVariantResponse, 'colorName' | 'colorHex'>[];
  assignments: FilamentProfileAssignmentInput[];
  issues?: FilamentProfileAssignmentIssue[];
  onChange: (value: FilamentProfileAssignmentInput[]) => void;
}

/** Default filament profile per printer model and nozzle. Per-color overrides live in each variant. */
export function ProfileAssignmentsCard({
  materialType,
  variants,
  assignments,
  issues = [],
  onChange,
}: Props) {
  const { fleetQuery, fleet, rows } = useAssignmentRows(assignments);
  function setDefault(row: AssignmentRow, profileId: string | null) {
    const retained = assignments.filter(
      (a) => !(matchesRow(a, row) && !a.colorHex),
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
              colorHex: null,
            },
          ]
        : retained,
    );
  }
  return (
    <Card>
      <CardHeader>
        <CardTitle>Profile assignments</CardTitle>
        <CardDescription>
          Required for production. Per-color profile: edit the variant.
        </CardDescription>
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
            title="Could not load printer models"
            onRetry={() => fleetQuery.refetch()}
          />
        )}
        {fleet && rows.length === 0 && (
          <p className="col-span-full text-sm text-muted-foreground">
            No printer nozzles reported. Assign profiles later.
          </p>
        )}
        {!!fleet?.printersAwaitingNozzle?.length && (
          <p className="col-span-full text-sm text-status-attention">
            Waiting for nozzle data: {fleet.printersAwaitingNozzle.join(', ')}.
          </p>
        )}
        {rows.map((row) => {
          const base = assignments.find(
            (a) => matchesRow(a, row) && !a.colorHex,
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
              (issue) => issue.assignment && matchesRow(issue.assignment, row),
            );
          const label = rowLabel(row);
          return (
            <div
              key={`${row.brand}-${row.modelCode}-${row.nozzleDiameterMm}`}
              className="min-w-0 rounded-lg border p-4 flex flex-col gap-3"
            >
              <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
                <span className="font-medium">{label}</span>
                <span className="flex-1 text-sm text-muted-foreground">
                  {row.printerCount
                    ? `${row.printerCount} printer${row.printerCount === 1 ? '' : 's'}`
                    : 'Not in fleet'}
                </span>
                {rowIssues.length ? (
                  <Badge kind="red">Invalid assignment</Badge>
                ) : base ? (
                  <Badge kind="green">Assigned</Badge>
                ) : (
                  <Badge kind="amber">Missing default</Badge>
                )}
              </div>
              {rowIssues.map((issue) => (
                <p
                  key={`${issue.assignment?.filamentProfileId}-${issue.assignment?.colorHex}`}
                  className="text-sm text-destructive"
                >
                  {issue.assignment?.colorHex
                    ? `${variants.find((v) => v.colorHex === issue.assignment?.colorHex)?.colorName ?? 'Color'}: `
                    : 'Default: '}
                  {issue.message}
                </p>
              ))}
              <ProfilePickerCombobox
                kind="filament"
                ariaLabel={`Default profile for ${label}`}
                value={base?.filamentProfileId}
                onChange={(id) => setDefault(row, id)}
                materialType={materialType}
                brand={row.brand}
                modelCode={row.modelCode}
                nozzleDiameterMm={row.nozzleDiameterMm}
              />
            </div>
          );
        })}
      </CardContent>
    </Card>
  );
}
