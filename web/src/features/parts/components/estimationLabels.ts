import type { EstimationDetails } from '@/lib/api/generated/endpoints.schemas';

/** Source key to the label shown for it; one place so the dialog and the part page cannot drift. */
export function estimationSourceLabel(source: string | null | undefined) {
  switch (source) {
    case 'DefaultProfiles':
      return 'Default profiles';
    case 'EmbeddedProject':
      return 'Project settings';
    case 'ProjectPresets':
      return 'Project presets';
    default:
      return 'Sliced file';
  }
}

/** The machine profile already names the model, so the model is shown only when there is no profile. */
export function estimationProfileNames(estimation: EstimationDetails) {
  return [
    estimation.machineProfileName ?? estimation.machineModel,
    estimation.processProfileName,
    ...(estimation.filamentProfileNames ?? []),
    estimation.buildPlate,
  ].filter(Boolean);
}

export const PRODUCTION_MAY_DIFFER = 'Production slicing may differ.';
