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
  return {
    version: `plan-${basis}-${quantity}`,
    plates,
    completeSets:
      basis === 'Sets' ? Math.floor((plates * perPlate) / perSet) : null,
    minutes: plates * minutes,
    grams: plates * 40,
    parts: [
      {
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
