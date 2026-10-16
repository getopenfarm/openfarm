import { useState } from 'react';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { Field, FieldGroup, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { useOpenFarmApiEndpointsAssembliesExplodeAssemblyEndpoint } from '@/lib/api/generated/assemblies/assemblies';

interface ExplosionResultViewProps {
  assemblyId: string;
}

/** FR-009 — printedParts/nonPrintedComponents rendered as two visually separate lists so no consumer can mistake one set for the other (FR-009d). */
export function ExplosionResultView({ assemblyId }: ExplosionResultViewProps) {
  const [quantity, setQuantity] = useState('1');
  const parsedQuantity = Number(quantity);
  const isValidQuantity =
    Number.isInteger(parsedQuantity) && parsedQuantity >= 1;

  const explosionQuery =
    useOpenFarmApiEndpointsAssembliesExplodeAssemblyEndpoint(
      assemblyId,
      { quantity: isValidQuantity ? parsedQuantity : 1 },
      { query: { enabled: isValidQuantity } },
    );
  const explosion = explosionQuery.data?.data;

  return (
    <div className="flex flex-col gap-4">
      <FieldGroup className="flex items-end gap-2">
        <Field className="flex flex-col gap-1.5">
          <FieldLabel htmlFor="explosion-quantity">Build quantity</FieldLabel>
          <Input
            id="explosion-quantity"
            type="number"
            min={1}
            step={1}
            value={quantity}
            onChange={(e) => setQuantity(e.target.value)}
            className="w-28"
          />
        </Field>
      </FieldGroup>

      {!isValidQuantity ? (
        <p className="text-sm text-destructive">
          Quantity must be a whole number of at least 1.
        </p>
      ) : explosionQuery.isLoading ? (
        <Skeleton className="h-32 w-full" />
      ) : (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <div>
            <h4 className="mb-2 text-sm font-medium">Printed parts</h4>
            {explosion?.printedParts?.length ? (
              <ul className="flex flex-col gap-1">
                {explosion.printedParts.map((part) => (
                  <li
                    key={part.partId}
                    className="flex justify-between text-sm"
                  >
                    <span>{part.partName}</span>
                    <span className="font-mono">{part.quantity}</span>
                  </li>
                ))}
              </ul>
            ) : (
              <EmptyState size="sm" bordered={false} title="No printed parts" />
            )}
          </div>

          <div>
            <h4 className="mb-2 text-sm font-medium">Non-printed components</h4>
            {explosion?.nonPrintedComponents?.length ? (
              <ul className="flex flex-col gap-1">
                {explosion.nonPrintedComponents.map((component) => (
                  <li
                    key={component.nonPrintedComponentId}
                    className="flex justify-between text-sm"
                  >
                    <span>
                      {component.name} ({component.unit})
                    </span>
                    <span className="font-mono">{component.quantity}</span>
                  </li>
                ))}
              </ul>
            ) : (
              <EmptyState
                size="sm"
                bordered={false}
                title="No non-printed components"
              />
            )}
          </div>
        </div>
      )}
    </div>
  );
}
