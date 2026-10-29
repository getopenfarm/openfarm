import { useMutation, useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { useRef, useState } from 'react';
import { Link } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableRowActions } from '@/components/data-table/DataTableRowActions';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import {
  DropdownMenuGroup,
  DropdownMenuItem,
} from '@/components/ui/dropdown-menu';
import { Skeleton } from '@/components/ui/skeleton';
import { useCurrency } from '@/features/settings/generalSettings';
import type { PrinterModelCostResponse } from '@/lib/api/generated/endpoints.schemas';
import { httpClient } from '@/lib/api/httpClient';
import { formatCost, formatHours, formatWatts } from '@/lib/formatUnits';
import {
  invalidatePrinterCosts,
  modelKey,
  printerCostsKey,
  usePrinterCostCatalog,
  usePrinterCosts,
} from './printerCosts';

export const optionalCostNumber = z
  .string()
  .refine(
    (v) => v.trim() === '' || (Number.isFinite(Number(v)) && Number(v) >= 0),
    'Enter a nonnegative number.',
  );
export const printerCostPath = (brand?: string, code?: string) =>
  `/settings/printer-costs/models/${encodeURIComponent(brand ?? '')}/${encodeURIComponent(code ?? '')}`;

export function PrinterCostsPage() {
  const currency = useCurrency();
  const amount = (n: number | null | undefined) =>
    n == null ? 'Not priced' : formatCost(n, currency);
  const query = usePrinterCosts();
  const client = useQueryClient();
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [removing, setRemoving] = useState<PrinterModelCostResponse | null>(
    null,
  );
  const returnFocusRef = useRef<HTMLButtonElement | null>(null);
  const fallbackFocusRef = useRef<HTMLAnchorElement | null>(null);
  const remove = useMutation({
    mutationFn: (model: PrinterModelCostResponse) =>
      httpClient(
        `${printerCostsKey}/models/${encodeURIComponent(model.brand ?? '')}/${encodeURIComponent(model.modelCode ?? '')}`,
        { method: 'DELETE' },
      ),
    onError: (error) => {
      if (error instanceof Response && error.status === 409)
        invalidatePrinterCosts(client);
    },
    onSuccess: (_, model) => {
      client.setQueryData<{ data: { models?: PrinterModelCostResponse[] } }>(
        [printerCostsKey],
        (previous) =>
          previous
            ? {
                ...previous,
                data: {
                  ...previous.data,
                  models: previous.data.models?.filter(
                    (m) =>
                      modelKey(m.brand, m.modelCode) !==
                      modelKey(model.brand, model.modelCode),
                  ),
                },
              }
            : previous,
      );
      returnFocusRef.current = null;
      setConfirmOpen(false);
      setRemoving(null);
      invalidatePrinterCosts(client);
      toast.success('Printer model costs removed.');
    },
  });
  const catalog = usePrinterCostCatalog();
  const names = new Map(
    (catalog.data?.data.brands ?? []).flatMap((b) =>
      (b.models ?? []).map((m) => [modelKey(b.brand, m.modelCode), m.name]),
    ),
  );
  const columns: ColumnDef<PrinterModelCostResponse>[] = [
    {
      accessorKey: 'modelCode',
      header: 'Printer model',
      cell: ({ row }) => (
        <Link to={printerCostPath(row.original.brand, row.original.modelCode)}>
          {names.get(modelKey(row.original.brand, row.original.modelCode)) ??
            `${row.original.brand} ${row.original.modelCode}`}
        </Link>
      ),
    },
    {
      accessorKey: 'printerPurchasePrice',
      header: 'Purchase price',
      cell: ({ row }) => amount(row.original.printerPurchasePrice),
    },
    {
      accessorKey: 'printerUpfrontCost',
      header: 'Printer upfront cost',
      cell: ({ row }) => amount(row.original.printerUpfrontCost),
    },
    {
      accessorKey: 'lifetimeServiceCost',
      header: 'Lifetime service cost',
      cell: ({ row }) => amount(row.original.lifetimeServiceCost),
    },
    {
      accessorKey: 'lifetimePrintingHours',
      header: 'Lifetime printing hours',
      cell: ({ row }) =>
        row.original.lifetimePrintingHours == null
          ? 'Not configured'
          : formatHours(row.original.lifetimePrintingHours),
    },
    {
      accessorKey: 'averagePrintingWatts',
      header: 'Printing power',
      cell: ({ row }) =>
        row.original.averagePrintingWatts == null
          ? 'Not configured'
          : formatWatts(row.original.averagePrintingWatts),
    },
    {
      accessorKey: 'depreciationPerHour',
      header: 'Depreciation / h',
      cell: ({ row }) => amount(row.original.depreciationPerHour),
    },
    {
      accessorKey: 'electricityPerHour',
      header: 'Electricity / h',
      cell: ({ row }) => amount(row.original.electricityPerHour),
    },
    {
      id: 'actions',
      header: '',
      cell: ({ row }) => (
        <DataTableRowActions
          label={`${row.original.brand} ${row.original.modelCode}`}
          disabled={remove.isPending}
          triggerRef={(node) => {
            if (
              removing &&
              modelKey(removing.brand, removing.modelCode) ===
                modelKey(row.original.brand, row.original.modelCode)
            )
              returnFocusRef.current = node;
          }}
          onOpen={(trigger) => {
            returnFocusRef.current = trigger;
          }}
        >
          <DropdownMenuGroup>
            <DropdownMenuItem asChild>
              <Link
                to={printerCostPath(row.original.brand, row.original.modelCode)}
              >
                Edit costs
              </Link>
            </DropdownMenuItem>
            {row.original.canRemove === true && (
              <DropdownMenuItem
                variant="destructive"
                onSelect={() => {
                  remove.reset();
                  setRemoving(row.original);
                  setConfirmOpen(true);
                }}
              >
                Remove
              </DropdownMenuItem>
            )}
          </DropdownMenuGroup>
        </DataTableRowActions>
      ),
    },
  ];
  return (
    <Page>
      <PageHeader
        title="Printer costs"
        size="section"
        description="Model costs remain available after printers are removed."
        action={
          <Button asChild>
            <Link to="new" ref={fallbackFocusRef}>
              Add printer model
            </Link>
          </Button>
        }
      />
      {query.isPending ? (
        <Skeleton className="h-40" />
      ) : query.isError ? (
        <ErrorState
          title="Could not load printer costs"
          onRetry={() => void query.refetch()}
        />
      ) : (
        <>
          <p>
            Configure the electricity tariff in{' '}
            <Link to="/settings/general">General settings</Link>.
          </p>
          <DataTable
            columns={columns}
            data={query.data.data.models ?? []}
            getRowId={(r) => modelKey(r.brand, r.modelCode)}
            emptyState={
              <EmptyState
                title="No printer models"
                description="Add a supported model or register a printer to configure its costs."
              />
            }
          />
        </>
      )}
      <ConfirmDialog
        open={confirmOpen}
        onOpenChange={(open) => {
          if (!open && !remove.isPending) {
            setConfirmOpen(false);
            remove.reset();
          }
        }}
        title="Remove printer model costs?"
        description={`Remove cost settings for ${removing?.brand ?? ''} ${removing?.modelCode ?? ''}? Estimates using this model will be incomplete until its costs are configured again.`}
        confirmLabel="Remove"
        hideConfirm={
          remove.error instanceof Response && remove.error.status === 409
        }
        destructive
        closeOnConfirm={false}
        isPending={remove.isPending}
        onConfirm={() => {
          if (removing && !remove.isPending) remove.mutate(removing);
        }}
        error={
          remove.isError
            ? remove.error instanceof Response && remove.error.status === 409
              ? 'This model is now used by a printer in the fleet. Remove its printers before removing its cost settings.'
              : 'Could not remove printer model costs. Try again.'
            : undefined
        }
        returnFocusRef={returnFocusRef}
        fallbackFocusRef={fallbackFocusRef}
      />
    </Page>
  );
}
