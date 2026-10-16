import { PlusIcon, TrashIcon } from 'lucide-react';
import { useState } from 'react';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { Button } from '@/components/ui/button';
import { Field, FieldGroup, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useOpenFarmApiEndpointsAssembliesListAssembliesEndpoint } from '@/lib/api/generated/assemblies/assemblies';
import type {
  ListAssembliesResponse,
  ListNonPrintedComponentsResponse,
  ListPartsResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsNonPrintedComponentsListNonPrintedComponentsEndpoint } from '@/lib/api/generated/non-printed-components/non-printed-components';
import { useOpenFarmApiEndpointsPartsListPartsEndpoint } from '@/lib/api/generated/parts/parts';

export type BomLineTargetKind = 'Part' | 'Assembly' | 'NonPrintedComponent';

export interface EditableLine {
  /** Stable client-side key — the server line id once persisted, otherwise a generated temp id. */
  key: string;
  /** Set once this line has been persisted (edit mode) — its target is immutable, quantity-only edits allowed. */
  lineId?: string;
  targetKind: BomLineTargetKind;
  targetId: string;
  targetName: string;
  quantity: number;
}

const TARGET_KIND_LABELS: Record<BomLineTargetKind, string> = {
  Part: 'Part',
  Assembly: 'Assembly',
  NonPrintedComponent: 'Non-printed component',
};

function wholeNumberQuantityFor(kind: BomLineTargetKind) {
  return kind !== 'NonPrintedComponent';
}

interface BomLineEditorProps {
  lines: EditableLine[];
  onChange: (lines: EditableLine[]) => void;
  /** Excludes this assembly from the Assembly-target picker (edit mode — a self-reference is always rejected server-side, but hiding it is better UX). */
  excludeAssemblyId?: string;
}

export function BomLineEditor({
  lines,
  onChange,
  excludeAssemblyId,
}: BomLineEditorProps) {
  const [draftKind, setDraftKind] = useState<BomLineTargetKind>('Part');
  const [draftTargetId, setDraftTargetId] = useState('');
  const [draftQuantity, setDraftQuantity] = useState('1');

  const partsQuery = useOpenFarmApiEndpointsPartsListPartsEndpoint({
    page: 1,
    pageSize: 500,
  });
  const parts =
    (partsQuery.data?.data as ListPartsResponse | undefined)?.parts ?? [];

  const assembliesQuery =
    useOpenFarmApiEndpointsAssembliesListAssembliesEndpoint({
      page: 1,
      pageSize: 500,
    });
  const assemblies = (
    (assembliesQuery.data?.data as ListAssembliesResponse | undefined)?.items ??
    []
  ).filter((a) => a.id !== excludeAssemblyId);

  const componentsQuery =
    useOpenFarmApiEndpointsNonPrintedComponentsListNonPrintedComponentsEndpoint(
      { page: 1, pageSize: 500 },
    );
  const components =
    (componentsQuery.data?.data as ListNonPrintedComponentsResponse | undefined)
      ?.items ?? [];

  const draftOptions =
    draftKind === 'Part'
      ? parts.map((p) => ({ id: p.id ?? '', name: p.name ?? '' }))
      : draftKind === 'Assembly'
        ? assemblies.map((a) => ({ id: a.id ?? '', name: a.name ?? '' }))
        : components.map((c) => ({ id: c.id ?? '', name: c.name ?? '' }));

  function resetDraft() {
    setDraftTargetId('');
    setDraftQuantity('1');
  }

  function handleAdd() {
    const quantity = Number(draftQuantity);
    if (!draftTargetId || Number.isNaN(quantity) || quantity <= 0) return;
    if (
      lines.some(
        (l) => l.targetKind === draftKind && l.targetId === draftTargetId,
      )
    ) {
      return;
    }

    const option = draftOptions.find((o) => o.id === draftTargetId);
    onChange([
      ...lines,
      {
        key: `new-${crypto.randomUUID()}`,
        targetKind: draftKind,
        targetId: draftTargetId,
        targetName: option?.name ?? '',
        quantity,
      },
    ]);
    resetDraft();
  }

  function handleQuantityChange(key: string, value: string) {
    const quantity = Number(value);
    onChange(lines.map((l) => (l.key === key ? { ...l, quantity } : l)));
  }

  function handleRemove(key: string) {
    onChange(lines.filter((l) => l.key !== key));
  }

  return (
    <div className="flex flex-col gap-3">
      {lines.length === 0 ? (
        <EmptyState size="sm" bordered={false} title="No lines" />
      ) : (
        <div className="flex flex-col gap-2">
          {lines.map((line) => (
            <div
              key={line.key}
              className="flex items-center gap-2 rounded-md border p-2"
            >
              <span className="w-40 shrink-0 text-xs text-muted-foreground">
                {TARGET_KIND_LABELS[line.targetKind]}
              </span>
              <span className="flex-1 truncate text-sm font-medium">
                {line.targetName || line.targetId}
              </span>
              <Input
                type="number"
                min={wholeNumberQuantityFor(line.targetKind) ? 1 : 0.001}
                step={wholeNumberQuantityFor(line.targetKind) ? 1 : 'any'}
                value={line.quantity}
                onChange={(e) => handleQuantityChange(line.key, e.target.value)}
                className="w-24"
                aria-label={`Quantity for ${line.targetName}`}
              />
              <Button
                type="button"
                variant="ghost"
                size="icon"
                onClick={() => handleRemove(line.key)}
                aria-label={`Remove ${line.targetName}`}
              >
                <TrashIcon data-icon="inline-start" className="" />
              </Button>
            </div>
          ))}
        </div>
      )}

      <FieldGroup className="flex items-end gap-2 border-t pt-3">
        <Field className="flex flex-col gap-1.5">
          <FieldLabel htmlFor="bom-line-target-kind">Target kind</FieldLabel>
          <Select
            value={draftKind}
            onValueChange={(value) => {
              setDraftKind(value as BomLineTargetKind);
              resetDraft();
            }}
          >
            <SelectTrigger id="bom-line-target-kind" className="w-44">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem value="Part">Part</SelectItem>
                <SelectItem value="Assembly">Assembly</SelectItem>
                <SelectItem value="NonPrintedComponent">
                  Non-printed component
                </SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
        </Field>

        <Field className="flex flex-1 flex-col gap-1.5">
          <FieldLabel htmlFor="bom-line-target">Target</FieldLabel>
          <Select value={draftTargetId} onValueChange={setDraftTargetId}>
            <SelectTrigger id="bom-line-target" className="w-full">
              <SelectValue placeholder="Choose a target" />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                {draftOptions.map((option) => (
                  <SelectItem key={option.id} value={option.id}>
                    {option.name}
                  </SelectItem>
                ))}
              </SelectGroup>
            </SelectContent>
          </Select>
        </Field>

        <div className="flex flex-col gap-1.5">
          <span className="text-xs text-muted-foreground">Quantity</span>
          <Input
            type="number"
            min={wholeNumberQuantityFor(draftKind) ? 1 : 0.001}
            step={wholeNumberQuantityFor(draftKind) ? 1 : 'any'}
            value={draftQuantity}
            onChange={(e) => setDraftQuantity(e.target.value)}
            className="w-24"
          />
        </div>

        <Button type="button" onClick={handleAdd} disabled={!draftTargetId}>
          <PlusIcon data-icon="inline-start" /> Add line
        </Button>
      </FieldGroup>
    </div>
  );
}
