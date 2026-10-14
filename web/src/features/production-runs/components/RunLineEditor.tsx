import { PlusIcon, TrashIcon } from 'lucide-react';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
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
  ListPartsResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsPartsListPartsEndpoint } from '@/lib/api/generated/parts/parts';
import { useOpenFarmApiEndpointsPrintersListFleetEndpoint } from '@/lib/api/generated/printers/printers';

export type RunLineTargetKind = 'Part' | 'Assembly';
export type RunLineAssignmentMode = 'Unassigned' | 'Pinned';
export type RunLineRevisionMode = 'Snapshot' | 'FollowLatest';

export interface EditableRunLine {
  /** Stable client-side key. */
  key: string;
  targetKind: RunLineTargetKind;
  targetId: string;
  targetName: string;
  /** null = unlimited/backfill (FR-006). */
  quantity: number | null;
  assignmentMode: RunLineAssignmentMode;
  pinnedPrinterId?: string;
  /** FR-009 — Snapshot pins the revision at queue time; Follow-latest re-reads it at dispatch time. */
  revisionMode: RunLineRevisionMode;
  /** Unlimited lines only: max printers backfilling this line at once. */
  backfillPrinterCap?: number;
  /** Unlimited lines only: stop backfilling after this many units; null = no ceiling. */
  backfillCumulativeUnitCeiling?: number | null;
}

interface RunLineEditorProps {
  lines: EditableRunLine[];
  onChange: (lines: EditableRunLine[]) => void;
}

/** Target-kind picker (Part/Assembly), quantity (blank = unlimited), assignment mode, pinned-printer picker (shown only when Pinned) — tasks.md T066. */
export function RunLineEditor({ lines, onChange }: RunLineEditorProps) {
  const [draftKind, setDraftKind] = useState<RunLineTargetKind>('Part');
  const [draftTargetId, setDraftTargetId] = useState('');
  const [draftUnlimited, setDraftUnlimited] = useState(false);
  const [draftQuantity, setDraftQuantity] = useState('1');
  const [draftAssignmentMode, setDraftAssignmentMode] =
    useState<RunLineAssignmentMode>('Unassigned');
  const [draftPinnedPrinterId, setDraftPinnedPrinterId] = useState('');
  const [draftPrinterCap, setDraftPrinterCap] = useState('1');
  const [draftCeiling, setDraftCeiling] = useState('');
  const draftRevisionMode: RunLineRevisionMode = draftUnlimited
    ? 'FollowLatest'
    : 'Snapshot';

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
  const assemblies =
    (assembliesQuery.data?.data as ListAssembliesResponse | undefined)?.items ??
    [];

  const printersQuery = useOpenFarmApiEndpointsPrintersListFleetEndpoint();
  const printers = printersQuery.data?.data?.printers ?? [];

  const draftOptions =
    draftKind === 'Part'
      ? parts.map((p) => ({ id: p.id ?? '', name: p.name ?? '' }))
      : assemblies.map((a) => ({ id: a.id ?? '', name: a.name ?? '' }));

  function resetDraft() {
    setDraftTargetId('');
    setDraftUnlimited(false);
    setDraftQuantity('1');
    setDraftAssignmentMode('Unassigned');
    setDraftPinnedPrinterId('');
    setDraftPrinterCap('1');
    setDraftCeiling('');
  }

  function handleAdd() {
    if (!draftTargetId) return;
    if (draftAssignmentMode === 'Pinned' && !draftPinnedPrinterId) return;

    let quantity: number | null = null;
    let backfill: Partial<EditableRunLine> = {};
    if (!draftUnlimited) {
      quantity = Number(draftQuantity);
      if (Number.isNaN(quantity) || quantity < 1) return;
    } else {
      const cap = Number(draftPrinterCap);
      const ceiling = draftCeiling.trim() === '' ? null : Number(draftCeiling);
      if (!Number.isInteger(cap) || cap < 1) return;
      if (ceiling !== null && (!Number.isInteger(ceiling) || ceiling < 0))
        return;
      backfill = {
        backfillPrinterCap: cap,
        backfillCumulativeUnitCeiling: ceiling,
      };
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
        assignmentMode: draftAssignmentMode,
        pinnedPrinterId:
          draftAssignmentMode === 'Pinned' ? draftPinnedPrinterId : undefined,
        revisionMode: draftRevisionMode,
        ...backfill,
      },
    ]);
    resetDraft();
  }

  function handleRemove(key: string) {
    onChange(lines.filter((l) => l.key !== key));
  }

  return (
    <div className="flex flex-col gap-3">
      {lines.length === 0 ? (
        <p className="text-sm text-muted-foreground">No lines yet.</p>
      ) : (
        <div className="flex flex-col gap-2">
          {lines.map((line) => (
            <div
              key={line.key}
              className="flex items-center gap-2 rounded-md border p-2"
            >
              <span className="w-24 shrink-0 text-xs text-muted-foreground">
                {line.targetKind}
              </span>
              <span className="flex-1 truncate text-sm font-medium">
                {line.targetName || line.targetId}
              </span>
              <span className="w-24 shrink-0 text-xs text-muted-foreground">
                {line.quantity === null ? 'Unlimited' : `Qty ${line.quantity}`}
                {line.quantity === null &&
                  ` · cap ${line.backfillPrinterCap ?? 1}${line.backfillCumulativeUnitCeiling != null ? ` · ceiling ${line.backfillCumulativeUnitCeiling}` : ''}`}
              </span>
              <span className="w-28 shrink-0 text-xs text-muted-foreground">
                {line.assignmentMode === 'Pinned'
                  ? `Pinned · ${printers.find((p) => p.id === line.pinnedPrinterId)?.name ?? line.pinnedPrinterId}`
                  : 'Unassigned'}
              </span>
              <span className="w-24 shrink-0 text-xs text-muted-foreground">
                {line.revisionMode === 'FollowLatest'
                  ? 'Follow-latest'
                  : 'Snapshot'}
              </span>
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

      <FieldGroup className="flex flex-wrap items-end gap-2 border-t pt-3">
        <Field className="flex flex-col gap-1.5">
          <FieldLabel htmlFor="run-line-target-kind">Target kind</FieldLabel>
          <Select
            value={draftKind}
            onValueChange={(value) => {
              setDraftKind(value as RunLineTargetKind);
              setDraftTargetId('');
            }}
          >
            <SelectTrigger id="run-line-target-kind" className="w-36">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem value="Part">Part</SelectItem>
                <SelectItem value="Assembly">Assembly</SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
        </Field>

        <Field className="flex flex-1 flex-col gap-1.5">
          <FieldLabel htmlFor="run-line-target">Target</FieldLabel>
          <Select value={draftTargetId} onValueChange={setDraftTargetId}>
            <SelectTrigger id="run-line-target" className="w-full">
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
          <FieldGroup className="flex items-center gap-2">
            <Input
              type="number"
              min={1}
              step={1}
              value={draftQuantity}
              disabled={draftUnlimited}
              onChange={(e) => setDraftQuantity(e.target.value)}
              className="w-20"
            />
            <Field
              orientation="horizontal"
              className="flex items-center gap-1.5 text-xs text-muted-foreground"
            >
              <Checkbox
                id="run-line-unlimited"
                checked={draftUnlimited}
                onCheckedChange={(checked) =>
                  setDraftUnlimited(checked === true)
                }
              />
              <FieldLabel htmlFor="run-line-unlimited" className="font-normal">
                Unlimited
              </FieldLabel>
            </Field>
          </FieldGroup>
        </div>

        {draftUnlimited && (
          <>
            <Field className="flex flex-col gap-1.5">
              <FieldLabel htmlFor="run-line-backfill-cap">
                Backfill printer cap
              </FieldLabel>
              <Input
                id="run-line-backfill-cap"
                type="number"
                min={1}
                step={1}
                value={draftPrinterCap}
                onChange={(e) => setDraftPrinterCap(e.target.value)}
                className="w-24"
              />
            </Field>
            <Field className="flex flex-col gap-1.5">
              <FieldLabel htmlFor="run-line-backfill-ceiling">
                Cumulative unit ceiling
              </FieldLabel>
              <Input
                id="run-line-backfill-ceiling"
                type="number"
                min={0}
                step={1}
                placeholder="No ceiling"
                value={draftCeiling}
                onChange={(e) => setDraftCeiling(e.target.value)}
                className="w-32"
              />
            </Field>
          </>
        )}

        <Field className="flex flex-col gap-1.5">
          <FieldLabel htmlFor="run-line-assignment-mode">Assignment</FieldLabel>
          <Select
            value={draftAssignmentMode}
            onValueChange={(value) =>
              setDraftAssignmentMode(value as RunLineAssignmentMode)
            }
          >
            <SelectTrigger id="run-line-assignment-mode" className="w-36">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem value="Unassigned">Unassigned</SelectItem>
                <SelectItem value="Pinned">Pinned</SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
        </Field>

        <p className="text-sm text-muted-foreground">
          {draftUnlimited
            ? 'Unlimited runs use the latest library part for each dispatch. The part must stay in the library until the run is cancelled or closed.'
            : 'Finite jobs keep their own snapshot. Library changes and removal will not affect these jobs.'}
        </p>

        {draftAssignmentMode === 'Pinned' && (
          <Field className="flex flex-col gap-1.5">
            <FieldLabel htmlFor="run-line-pinned-printer">Printer</FieldLabel>
            <Select
              value={draftPinnedPrinterId}
              onValueChange={setDraftPinnedPrinterId}
            >
              <SelectTrigger id="run-line-pinned-printer" className="w-44">
                <SelectValue placeholder="Choose a printer" />
              </SelectTrigger>
              <SelectContent>
                <SelectGroup>
                  {printers.map((printer) => (
                    <SelectItem key={printer.id} value={printer.id ?? ''}>
                      {printer.name}
                    </SelectItem>
                  ))}
                </SelectGroup>
              </SelectContent>
            </Select>
          </Field>
        )}

        <Button type="button" onClick={handleAdd} disabled={!draftTargetId}>
          <PlusIcon data-icon="inline-start" /> Add line
        </Button>
      </FieldGroup>
    </div>
  );
}
