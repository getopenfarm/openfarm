/** Fake server response for UI tests; production calculations are tested against the real API. */
export function productionPlanFixture(
  quantity: number,
  basis: string,
  perPlate = 4,
  perSet = 2,
  minutes = 60,
) {
  const required =
    basis === 'Plates'
      ? quantity * perPlate
      : basis === 'Sets'
        ? quantity * perSet
        : quantity;
  const plates = basis === 'Plates' ? quantity : Math.ceil(required / perPlate);
  const estimate = {
    time: { value: plates * minutes, missing: 0 },
    filament: { value: plates * 40, missing: 0 },
    approximate: false,
    colors: [],
  };
  return {
    estimate,
    version: `plan-${basis}-${quantity}`,
    plates,
    completeSets:
      basis === 'Sets' ? Math.floor((plates * perPlate) / perSet) : null,
    minutes: plates * minutes,
    grams: plates * 40,
    parts: [
      {
        estimate,
        revision: 'fixture',
        updatedAtUtc: '2026-11-01T00:00:00Z',
        jobUnits: Array.from({ length: plates }, () => perPlate),
        objectMappingVersion: null,
        partId: 'part-1',
        name: 'Bracket',
        requiredUnits: required,
        unitsPerPlate: perPlate,
        plates,
        plannedUnits: plates * perPlate,
        surplusUnits: plates * perPlate - required,
        minutes: plates * minutes,
        grams: plates * 40,
      },
    ],
  };
}
