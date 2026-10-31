import type {
  ProductionDemandResponse as GeneratedDemand,
  ProductionPlan as GeneratedPlan,
  ProductionPlanPart as GeneratedPlanPart,
} from '@/lib/api/generated/endpoints.schemas';
import {
  openFarmApiEndpointsProductionRunsChangeBackfillEndpoint as change,
  openFarmApiEndpointsProductionRunsListBackfillEndpoint as list,
  openFarmApiEndpointsProductionRunsPreviewProductionPlanEndpoint as preview,
  openFarmApiEndpointsProductionRunsSubmitProductionDemandEndpoint as submit,
} from '@/lib/api/generated/production-demands/production-demands';

export type QuantityBasis = 'Plates' | 'Units' | 'Sets';
export interface PlanLine {
  targetKind: 'Part' | 'Assembly';
  targetId: string;
  quantity: number | null;
  quantityBasis?: QuantityBasis;
  assignmentMode?: string;
  pinnedPrinterId?: string | null;
  backfillPrinterCap?: number;
}
export type PlanPart = Required<GeneratedPlanPart>;
export type ProductionPlan = Omit<Required<GeneratedPlan>, 'parts'> & {
  parts: PlanPart[];
};
export type DemandResponse = Required<GeneratedDemand>;
export const demandKey = '/api/v1/production-demands';
export async function previewPlan(
  line: PlanLine,
  plateOverrides?: Record<string, number>,
  signal?: AbortSignal,
) {
  const result = await preview({ line, plateOverrides }, { signal });
  const response = result.data as ProductionPlan;
  if (!response || !Array.isArray(response.parts))
    throw new Error('Could not read the production plan.');
  return response;
}
export async function submitDemand(data: {
  submissionId: string;
  line: PlanLine;
  planVersion?: string;
  productionRunId?: string;
  createRun?: boolean;
  plateOverrides?: Record<string, number>;
}) {
  return (await submit(data)).data as DemandResponse;
}
export async function listBackfill(signal?: AbortSignal) {
  return (await list({ signal })).data as DemandResponse[];
}
export async function changeBackfill({
  id,
  ...data
}: {
  id: string;
  action: string;
  printerCap?: number;
  pinnedPrinterId?: string | null;
}) {
  return change(id, data);
}
