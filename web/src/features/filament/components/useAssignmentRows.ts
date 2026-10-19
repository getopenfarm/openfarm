import type {
  FilamentAssignmentFleet,
  FilamentProfileAssignmentInput,
  FleetFilamentVariant,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsFilamentsGetFilamentAssignmentFleetEndpoint } from '@/lib/api/generated/filaments/filaments';

export type AssignmentRow = FleetFilamentVariant;

/** True when the assignment targets this printer model and nozzle (any color). */
export function matchesRow(
  assignment: Pick<
    FilamentProfileAssignmentInput,
    'brand' | 'modelCode' | 'nozzleDiameterMm'
  >,
  row: AssignmentRow,
) {
  return (
    assignment.brand === row.brand &&
    assignment.modelCode === row.modelCode &&
    assignment.nozzleDiameterMm === row.nozzleDiameterMm
  );
}

export function rowLabel(row: AssignmentRow) {
  return `${row.modelCode} · ${row.nozzleDiameterMm} mm`;
}

/** Fleet model/nozzle rows, plus rows for saved assignments whose hardware has left the fleet. */
export function useAssignmentRows(
  assignments: FilamentProfileAssignmentInput[],
) {
  const fleetQuery =
    useOpenFarmApiEndpointsFilamentsGetFilamentAssignmentFleetEndpoint();
  const fleet = fleetQuery.data?.data as FilamentAssignmentFleet | undefined;
  const rows: AssignmentRow[] = [...(fleet?.variants ?? [])];
  for (const assignment of assignments) {
    if (!rows.some((row) => matchesRow(assignment, row))) {
      rows.push({
        brand: assignment.brand,
        modelCode: assignment.modelCode,
        nozzleDiameterMm: assignment.nozzleDiameterMm,
        printerCount: 0,
      });
    }
  }
  return { fleetQuery, fleet, rows };
}
