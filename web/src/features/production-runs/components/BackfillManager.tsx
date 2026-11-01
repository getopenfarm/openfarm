import { useMutation, useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { useRef, useState } from 'react';
import { Link } from 'react-router';
import { toast } from 'sonner';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableRowActions } from '@/components/data-table/DataTableRowActions';
import { ErrorState } from '@/components/empty-state/EmptyState';
import { PermissionButton as Button } from '@/components/PermissionButton';
import { PermissionMenuItem as DropdownMenuItem } from '@/components/PermissionMenuItem';
import { Badge } from '@/components/ui/badge';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { DropdownMenuGroup } from '@/components/ui/dropdown-menu';
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
import { Skeleton } from '@/components/ui/skeleton';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import { useOpenFarmApiEndpointsPrintersListFleetEndpoint } from '@/lib/api/generated/printers/printers';
import { useAuthorizedQuery as useQuery } from '@/lib/auth/useAuthorizedQuery';

import { invalidateProductionQueries } from '../hooks/useProductionHub';
import {
  changeBackfill,
  type DemandResponse,
  demandKey,
  listBackfill,
} from './productionPlanner';
import { SendToQueueDialog } from './SendToQueueDialog';

export function BackfillManager() {
  const [open, setOpen] = useState(false);
  const [add, setAdd] = useState(false);
  const [editing, setEditing] = useState<DemandResponse | null>(null);
  const [confirm, setConfirm] = useState<{
    demand: DemandResponse;
    action: 'Remove' | 'Refresh';
  } | null>(null);
  const [cap, setCap] = useState('1');
  const [printer, setPrinter] = useState('automatic');
  const addTrigger = useRef<HTMLButtonElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const rowTrigger = useRef<HTMLButtonElement | null>(null);
  const client = useQueryClient();
  const entries = useQuery({
    queryKey: [demandKey, 'backfill'],
    enabled: open,
    queryFn: ({ signal }) => listBackfill(signal),
  });
  const printers =
    useOpenFarmApiEndpointsPrintersListFleetEndpoint({
      query: { enabled: !!editing },
    }).data?.data?.printers ?? [];
  const change = useMutation({
    mutationFn: changeBackfill,
    onSuccess: () => {
      invalidateProductionQueries(client);
      setConfirm(null);
      setEditing(null);
      toast.success('Backfill updated.');
    },
    onError: async (failure) =>
      toast.error(
        await extractErrorDetail(failure, 'Could not update backfill.'),
      ),
  });
  const columns: ColumnDef<DemandResponse>[] = [
    {
      accessorKey: 'name',
      header: 'Part or assembly',
      cell: ({ row }) => (
        <Link
          className="hover:underline"
          to={`/${row.original.targetKind === 'Part' ? 'parts' : 'assemblies'}/${row.original.targetId}`}
        >
          {row.original.name}
        </Link>
      ),
    },
    {
      accessorKey: 'state',
      header: 'State',
      cell: ({ row }) => (
        <Badge variant="secondary">{row.original.state}</Badge>
      ),
    },
    { accessorKey: 'printerCap', header: 'Printers at once' },
    {
      id: 'actions',
      cell: ({ row }) => (
        <DataTableRowActions
          label={row.original.name}
          disabled={change.isPending}
          onOpen={(element) => {
            rowTrigger.current = element;
          }}
        >
          <DropdownMenuGroup>
            <DropdownMenuItem
              permission="production-runs:edit"
              onSelect={() =>
                change.mutate({
                  id: row.original.id,
                  action: row.original.state === 'Paused' ? 'Enable' : 'Pause',
                })
              }
            >
              {row.original.state === 'Paused' ? 'Enable' : 'Pause'}
            </DropdownMenuItem>
            <DropdownMenuItem
              onSelect={() => {
                setEditing(row.original);
                setCap(String(row.original.printerCap));
                setPrinter(row.original.pinnedPrinterId ?? 'automatic');
              }}
            >
              Configure
            </DropdownMenuItem>
            {row.original.targetKind === 'Assembly' && (
              <DropdownMenuItem
                onSelect={() =>
                  setConfirm({ demand: row.original, action: 'Refresh' })
                }
              >
                Refresh component recipe
              </DropdownMenuItem>
            )}
            <DropdownMenuItem
              permission="production-runs:edit"
              variant="destructive"
              onSelect={() =>
                setConfirm({ demand: row.original, action: 'Remove' })
              }
            >
              Remove
            </DropdownMenuItem>
          </DropdownMenuGroup>
        </DataTableRowActions>
      ),
    },
  ];
  return (
    <>
      <Button ref={trigger} variant="outline" onClick={() => setOpen(true)}>
        Manage backfill
      </Button>
      <Dialog
        open={open}
        onOpenChange={(value) => {
          if (!change.isPending) setOpen(value);
        }}
      >
        <DialogContent
          className="max-h-[calc(100dvh-2rem)] overflow-y-auto sm:max-w-3xl"
          onCloseAutoFocus={(event) => {
            event.preventDefault();
            trigger.current?.focus();
          }}
        >
          <DialogHeader>
            <DialogTitle>Continuous backfill</DialogTitle>
            <DialogDescription>
              These entries fill compatible idle capacity after finite work.
              Pausing prevents new dispatches; current prints continue.
            </DialogDescription>
          </DialogHeader>
          <Button
            permission="production-runs:edit"
            ref={addTrigger}
            variant="outline"
            onClick={() => setAdd(true)}
          >
            Add backfill
          </Button>
          {entries.isPending ? (
            <Skeleton className="h-24" />
          ) : entries.isError ? (
            <ErrorState
              title="Could not load backfill"
              onRetry={() => entries.refetch()}
            />
          ) : (
            <DataTable
              columns={columns}
              data={Array.isArray(entries.data) ? entries.data : []}
              pageSize={25}
              getRowId={(entry) => entry.id}
            />
          )}
        </DialogContent>
      </Dialog>
      {add && (
        <SendToQueueDialog initialBackfill onClose={() => setAdd(false)} />
      )}
      <Dialog
        open={!!editing}
        onOpenChange={(value) => {
          if (!value && !change.isPending) setEditing(null);
        }}
      >
        <DialogContent
          onCloseAutoFocus={(event) => {
            if (rowTrigger.current?.isConnected) {
              event.preventDefault();
              rowTrigger.current.focus();
            }
          }}
        >
          <DialogHeader>
            <DialogTitle>Configure backfill</DialogTitle>
            <DialogDescription>{editing?.name}</DialogDescription>
          </DialogHeader>
          <FieldGroup>
            <Field
              data-invalid={
                !Number.isSafeInteger(Number(cap)) || Number(cap) < 1
              }
            >
              <FieldLabel htmlFor="backfill-cap">Printers at once</FieldLabel>
              <Input
                id="backfill-cap"
                className="w-28"
                type="number"
                min={1}
                step={1}
                value={cap}
                onChange={(e) => setCap(e.target.value)}
                disabled={change.isPending}
              />
            </Field>
            <Field>
              <FieldLabel htmlFor="backfill-printer">Printer</FieldLabel>
              <Select
                value={printer}
                onValueChange={setPrinter}
                disabled={change.isPending}
              >
                <SelectTrigger id="backfill-printer">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    <SelectItem value="automatic">Automatic</SelectItem>
                    {printers.map((p) => (
                      <SelectItem key={p.id} value={p.id ?? ''}>
                        {p.name}
                      </SelectItem>
                    ))}
                  </SelectGroup>
                </SelectContent>
              </Select>
            </Field>
          </FieldGroup>
          <DialogFooter>
            <Button
              variant="outline"
              disabled={change.isPending}
              onClick={() => setEditing(null)}
            >
              Cancel
            </Button>
            <Button
              permission="production-runs:edit"
              disabled={
                change.isPending ||
                !Number.isSafeInteger(Number(cap)) ||
                Number(cap) < 1
              }
              onClick={() =>
                editing &&
                change.mutate({
                  id: editing.id,
                  action: 'Configure',
                  printerCap: Number(cap),
                  pinnedPrinterId: printer === 'automatic' ? null : printer,
                })
              }
            >
              Save
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
      <ConfirmDialog
        open={!!confirm}
        onOpenChange={(value) => {
          if (!value) setConfirm(null);
        }}
        title={
          confirm?.action === 'Refresh'
            ? 'Refresh component recipe?'
            : 'Remove backfill?'
        }
        description={
          confirm?.action === 'Refresh'
            ? 'Use the current Assembly BOM and start a new balancing period. Unsent plates are cancelled. Current prints and history are retained.'
            : 'Stop generating plates and cancel unsent plates. Current prints and history are retained.'
        }
        confirmLabel={
          confirm?.action === 'Refresh' ? 'Refresh recipe' : 'Remove'
        }
        destructive
        isPending={change.isPending}
        closeOnConfirm={false}
        returnFocusRef={addTrigger}
        fallbackFocusRef={addTrigger}
        onConfirm={() =>
          confirm &&
          change.mutate({ id: confirm.demand.id, action: confirm.action })
        }
      />
    </>
  );
}
