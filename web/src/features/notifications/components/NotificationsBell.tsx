import { useMutation, useQueryClient } from '@tanstack/react-query';
import { BellIcon, CheckIcon } from 'lucide-react';
import { useState } from 'react';
import { toast } from 'sonner';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover';
import { Skeleton } from '@/components/ui/skeleton';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type {
  ListFleetResponse,
  ListNotificationsResponse,
  RfidWeightDriftResponse,
  SpoolResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsNotificationsListNotificationsEndpointQueryKey,
  useOpenFarmApiEndpointsNotificationsDismissNotificationEndpoint,
  useOpenFarmApiEndpointsNotificationsListNotificationsEndpoint,
} from '@/lib/api/generated/notifications/notifications';
import { useOpenFarmApiEndpointsPrintersListFleetEndpoint } from '@/lib/api/generated/printers/printers';
import {
  openFarmApiEndpointsSpoolsEditSpoolEndpoint,
  openFarmApiEndpointsSpoolsGetSpoolEndpoint,
  useOpenFarmApiEndpointsSpoolsAttachSpoolRfidEndpoint,
} from '@/lib/api/generated/spools/spools';
import { invalidateFilamentTrackingQueries } from '@/lib/invalidateFilamentTrackingQueries';
import { badgeText, bellLabel, findLine } from '../lib/notifications';
import { AssignSpoolDialog } from './AssignSpoolDialog';
import {
  LowStockNotification,
  NotificationItem,
  RfidWeightDriftNotification,
  UnregisteredRfidSpoolNotification,
} from './NotificationItem';

/** Header bell: a count of what needs the operator, and a popover listing it with the action to settle each. */
export function NotificationsBell() {
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const [assigning, setAssigning] = useState<{
    printId: string;
    lineId: string;
  } | null>(null);

  const query = useOpenFarmApiEndpointsNotificationsListNotificationsEndpoint();
  const data = query.data?.data as ListNotificationsResponse | undefined;
  const items = data?.items ?? [];
  const lowStock = data?.lowStock ?? [];
  const rfidSpools = data?.rfidSpools ?? [];
  const rfidDrift = data?.rfidDrift ?? [];
  const count =
    (data?.totalCount ?? items.length) +
    lowStock.length +
    rfidSpools.length +
    rfidDrift.length;

  const fleet = useOpenFarmApiEndpointsPrintersListFleetEndpoint().data?.data as
    | ListFleetResponse
    | undefined;
  const printerName = (printerId?: string | null) =>
    fleet?.printers?.find((printer) => printer.id === printerId)?.name;

  const dismiss =
    useOpenFarmApiEndpointsNotificationsDismissNotificationEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Dismissed.');
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsNotificationsListNotificationsEndpointQueryKey(),
          });
        },
        onError: async (error) => {
          toast.error(await extractErrorDetail(error, 'Could not dismiss.'));
        },
      },
    });

  const link = useOpenFarmApiEndpointsSpoolsAttachSpoolRfidEndpoint({
    mutation: {
      onSuccess: () => {
        toast.success('Spool linked.');
        queryClient.invalidateQueries({
          queryKey:
            getOpenFarmApiEndpointsNotificationsListNotificationsEndpointQueryKey(),
        });
        invalidateFilamentTrackingQueries(queryClient);
      },
      onError: async (error) => {
        toast.error(await extractErrorDetail(error, 'Could not link spool.'));
      },
    },
  });

  // The spool edit replaces label and purchase info too, so they are read back first and kept as they are.
  const correct = useMutation({
    mutationFn: async (item: RfidWeightDriftResponse) => {
      const id = item.spoolId ?? '';
      const spool = (await openFarmApiEndpointsSpoolsGetSpoolEndpoint(id))
        .data as SpoolResponse;
      await openFarmApiEndpointsSpoolsEditSpoolEndpoint(id, {
        label: spool.label ?? null,
        remainingWeightGrams: item.readGrams ?? 0,
        reason: 'Printer reading',
        price: spool.price ?? null,
        supplier: spool.supplier ?? null,
        orderNumber: spool.orderNumber ?? null,
        receivedOn: spool.receivedOn ?? null,
      });
    },
    onSuccess: () => {
      toast.success('Spool corrected.');
      queryClient.invalidateQueries({
        queryKey:
          getOpenFarmApiEndpointsNotificationsListNotificationsEndpointQueryKey(),
      });
      invalidateFilamentTrackingQueries(queryClient);
    },
    onError: async (error) => {
      toast.error(await extractErrorDetail(error, 'Could not correct spool.'));
    },
  });

  const target = assigning
    ? findLine(items, assigning.printId, assigning.lineId)
    : undefined;

  return (
    <>
      <Popover open={open} onOpenChange={setOpen}>
        <PopoverTrigger asChild>
          <Button
            variant="ghost"
            size="icon"
            className="relative"
            aria-label={bellLabel(count)}
          >
            <BellIcon data-icon="inline-start" />
            {count > 0 && (
              <span
                aria-hidden
                className="absolute top-0.5 right-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-kind-amber px-1 text-[10px] leading-none font-medium text-white tabular-nums"
              >
                {badgeText(count)}
              </span>
            )}
          </Button>
        </PopoverTrigger>
        <PopoverContent
          align="end"
          className="w-[min(24rem,calc(100vw-2rem))] gap-0 p-0"
        >
          <div className="flex items-center justify-between gap-2 border-b px-3 py-2.5">
            <h2 className="text-sm font-medium">Action required</h2>
            {count > 0 && <Badge variant="secondary">{count}</Badge>}
          </div>
          <div className="max-h-96 overflow-y-auto">
            {query.isLoading ? (
              <Skeleton className="m-3 h-20" />
            ) : query.isError ? (
              <ErrorState
                size="sm"
                bordered={false}
                title="Could not load notifications"
                onRetry={() => query.refetch()}
              />
            ) : items.length === 0 &&
              lowStock.length === 0 &&
              rfidSpools.length === 0 &&
              rfidDrift.length === 0 ? (
              <EmptyState
                size="sm"
                bordered={false}
                icon={<CheckIcon className="size-6 text-muted-foreground" />}
                title="Nothing to do"
              />
            ) : (
              <ul className="divide-y">
                {items.map((item) => (
                  <NotificationItem
                    key={item.printId}
                    item={item}
                    printerName={printerName(item.printerId)}
                    dismissing={
                      dismiss.isPending &&
                      dismiss.variables?.id === item.printId
                    }
                    onAssign={(line) => {
                      // The dialog lives outside the popover, so the popover closes first.
                      setOpen(false);
                      setAssigning({
                        printId: item.printId ?? '',
                        lineId: line.lineId ?? '',
                      });
                    }}
                    onDismiss={() => dismiss.mutate({ id: item.printId ?? '' })}
                  />
                ))}
                {rfidSpools.map((item) => (
                  <UnregisteredRfidSpoolNotification
                    key={`${item.feedSlotId}-${item.rfidId}`}
                    item={item}
                    linking={
                      link.isPending &&
                      link.variables?.id === item.loadedSpoolId
                    }
                    onLink={() =>
                      link.mutate({
                        id: item.loadedSpoolId ?? '',
                        data: { rfidId: item.rfidId ?? '' },
                      })
                    }
                  />
                ))}
                {rfidDrift.map((item) => (
                  <RfidWeightDriftNotification
                    key={item.spoolId}
                    item={item}
                    correcting={
                      correct.isPending &&
                      correct.variables?.spoolId === item.spoolId
                    }
                    onCorrect={() => correct.mutate(item)}
                  />
                ))}
                {lowStock.map((item) => (
                  <LowStockNotification
                    key={item.filamentVariantId}
                    item={item}
                  />
                ))}
              </ul>
            )}
          </div>
        </PopoverContent>
      </Popover>
      {target && (
        <AssignSpoolDialog
          item={target.item}
          line={target.line}
          onOpenChange={(next) => {
            if (!next) setAssigning(null);
          }}
        />
      )}
    </>
  );
}
