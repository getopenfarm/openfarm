import { keepPreviousData, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { toast } from 'sonner';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Swatch } from '@/components/swatch/Swatch';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogClose,
  DialogDescription,
  DialogScrollableContent,
  DialogTitle,
} from '@/components/ui/dialog';
import { Spinner } from '@/components/ui/spinner';
import { useFilamentCatalog } from '@/features/filament/data/useFilamentCatalog';
import { SpoolPicker } from '@/features/spools/components/SpoolPicker';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import {
  type AssignPrintFilamentSpoolResponse,
  type ListSpoolsResponse,
  type NotificationItemResponse,
  type NotificationLineResponse,
  SpoolState,
} from '@/lib/api/generated/endpoints.schemas';
import { getOpenFarmApiEndpointsNotificationsListNotificationsEndpointQueryKey } from '@/lib/api/generated/notifications/notifications';
import { useOpenFarmApiEndpointsPrintsAssignPrintFilamentSpoolEndpoint } from '@/lib/api/generated/prints/prints';
import { useOpenFarmApiEndpointsSpoolsListSpoolsEndpoint } from '@/lib/api/generated/spools/spools';
import { formatGrams } from '@/lib/formatUnits';
import { lineLabel } from '../lib/notifications';

/** Spools fetched per search; a longer match list asks the operator to narrow the search. */
const PICKER_PAGE_SIZE = 50;

interface AssignSpoolDialogProps {
  item: NotificationItemResponse;
  line: NotificationLineResponse;
  onOpenChange: (open: boolean) => void;
}

/** Pick the loaded spool a line really drew from; it is deducted as automatic deduction would have. Mounted only while open. */
export function AssignSpoolDialog({
  item,
  line,
  onOpenChange,
}: AssignSpoolDialogProps) {
  const queryClient = useQueryClient();
  const [spoolId, setSpoolId] = useState('');
  const [query, setQuery] = useState('');
  const search = useDebouncedValue(query.trim(), 250);
  const catalog = useFilamentCatalog();

  // Deduction draws only from a loaded spool, so only those are offered.
  const spoolsQuery = useOpenFarmApiEndpointsSpoolsListSpoolsEndpoint(
    {
      state: SpoolState.Using,
      search: search || undefined,
      page: 1,
      pageSize: PICKER_PAGE_SIZE,
    },
    { query: { placeholderData: keepPreviousData } },
  );
  const listing = spoolsQuery.data?.data as ListSpoolsResponse | undefined;
  const candidates = listing?.spools ?? [];

  const assign = useOpenFarmApiEndpointsPrintsAssignPrintFilamentSpoolEndpoint({
    mutation: {
      onSuccess: (response) => {
        const spool = candidates.find((candidate) => candidate.id === spoolId);
        const grams = (response.data as AssignPrintFilamentSpoolResponse)
          ?.gramsDeducted;
        toast.success(
          grams != null && spool
            ? `Deducted ${formatGrams(grams)} from ${[spool.vendorName, spool.material, spool.colorName].filter(Boolean).join(' ')}.`
            : 'Spool assigned.',
        );
        onOpenChange(false);
        queryClient.invalidateQueries({
          queryKey:
            getOpenFarmApiEndpointsNotificationsListNotificationsEndpointQueryKey(),
        });
        queryClient.invalidateQueries({
          predicate: (candidate) =>
            typeof candidate.queryKey[0] === 'string' &&
            (candidate.queryKey[0].startsWith('/api/v1/spools') ||
              candidate.queryKey[0].startsWith('/api/v1/print-jobs')),
        });
      },
      onError: async (error) => {
        toast.error(await extractErrorDetail(error, 'Could not assign spool.'));
      },
    },
  });

  return (
    <Dialog open onOpenChange={onOpenChange}>
      <DialogScrollableContent
        className="sm:max-w-lg"
        header={
          <>
            <DialogTitle>Assign spool</DialogTitle>
            <DialogDescription className="flex items-center gap-2">
              <Swatch colorHex={line.colorHex ?? undefined} size="sm" />
              <span>
                {item.name} · {lineLabel(line)}
                {line.estimatedGrams != null &&
                  ` · ${formatGrams(line.estimatedGrams)} est.`}
              </span>
            </DialogDescription>
          </>
        }
        footer={
          <>
            <DialogClose asChild>
              <Button type="button" variant="outline">
                Cancel
              </Button>
            </DialogClose>
            <Button
              type="button"
              disabled={!spoolId || assign.isPending}
              onClick={() =>
                assign.mutate({
                  id: item.printId ?? '',
                  lineId: line.lineId ?? '',
                  data: { spoolId },
                })
              }
            >
              {assign.isPending && <Spinner data-icon="inline-start" />}
              {assign.isPending ? 'Assigning…' : 'Assign and deduct'}
            </Button>
          </>
        }
      >
        {spoolsQuery.isError ? (
          <div role="alert">
            <ErrorState
              size="sm"
              bordered={false}
              description="Could not load spools."
              onRetry={() => spoolsQuery.refetch()}
            />
          </div>
        ) : spoolsQuery.isLoading ? (
          <div role="status">
            <EmptyState
              size="sm"
              bordered={false}
              description="Loading spools…"
            />
          </div>
        ) : (
          <SpoolPicker
            spools={candidates}
            total={listing?.totalCount ?? candidates.length}
            catalog={catalog}
            query={query}
            onQueryChange={setQuery}
            refreshing={
              spoolsQuery.isPlaceholderData || query.trim() !== search
            }
            value={spoolId}
            onValueChange={setSpoolId}
            emptyTitle="No loaded spools"
            legend="Loaded spools"
          />
        )}
      </DialogScrollableContent>
    </Dialog>
  );
}
