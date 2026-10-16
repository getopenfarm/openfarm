import { useState } from 'react';
import { Field, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import {
  useOpenFarmApiEndpointsAssembliesCalculateBuildableUnitsEndpoint,
  useOpenFarmApiEndpointsAssembliesExplodeAssemblyEndpoint,
} from '@/lib/api/generated/assemblies/assemblies';

interface BuildableUnitsPanelProps {
  assemblyId: string;
}

/** FR-010 — a good-quantity input per printed part (from the quantity-1 explosion), rendering isApplicable/value/notApplicableReason distinctly so "not applicable" is never mistaken for 0. */
export function BuildableUnitsPanel({ assemblyId }: BuildableUnitsPanelProps) {
  const explosionQuery =
    useOpenFarmApiEndpointsAssembliesExplodeAssemblyEndpoint(assemblyId, {
      quantity: 1,
    });
  const printedParts = explosionQuery.data?.data?.printedParts ?? [];

  const [goodQuantities, setGoodQuantities] = useState<Record<string, string>>(
    {},
  );

  const buildableUnitsMutation =
    useOpenFarmApiEndpointsAssembliesCalculateBuildableUnitsEndpoint();

  function handleQuantityChange(partId: string, value: string) {
    const next = { ...goodQuantities, [partId]: value };
    setGoodQuantities(next);

    const goodQuantitiesPayload: Record<string, number> = {};
    for (const part of printedParts) {
      if (!part.partId) continue;
      const raw = next[part.partId];
      const parsed = Number(raw);
      goodQuantitiesPayload[part.partId] = Number.isFinite(parsed) ? parsed : 0;
    }

    buildableUnitsMutation.mutate({
      id: assemblyId,
      data: { goodQuantities: goodQuantitiesPayload },
    });
  }

  if (explosionQuery.isLoading) {
    return <Skeleton className="h-24 w-full" />;
  }

  if (printedParts.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        No printed parts. Buildable units not applicable.
      </p>
    );
  }

  const result = buildableUnitsMutation.data?.data;

  return (
    <div className="flex flex-col gap-4">
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        {printedParts.map((part) => (
          <Field key={part.partId} className="flex flex-col gap-1.5">
            <FieldLabel htmlFor={`good-qty-${part.partId}`}>
              {part.partName} on hand
            </FieldLabel>
            <Input
              id={`good-qty-${part.partId}`}
              type="number"
              min={0}
              value={goodQuantities[part.partId ?? ''] ?? ''}
              onChange={(e) =>
                part.partId && handleQuantityChange(part.partId, e.target.value)
              }
            />
          </Field>
        ))}
      </div>

      {result ? (
        <div className="rounded-md border p-3">
          {result.isApplicable ? (
            <p className="text-sm">
              Buildable units:{' '}
              <span className="font-mono text-base font-semibold">
                {result.value}
              </span>
            </p>
          ) : (
            <p className="text-sm text-muted-foreground">
              Not applicable — {result.notApplicableReason}
            </p>
          )}
        </div>
      ) : null}
    </div>
  );
}
