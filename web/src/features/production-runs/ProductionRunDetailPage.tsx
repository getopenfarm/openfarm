import { useQueryClient } from '@tanstack/react-query';
import { CheckIcon, XIcon } from 'lucide-react';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { BackLink } from '@/components/back-link/BackLink';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import {
  getOpenFarmApiEndpointsProductionRunsGetProductionRunEndpointQueryKey,
  getOpenFarmApiEndpointsProductionRunsListProductionRunsEndpointQueryKey,
  useOpenFarmApiEndpointsProductionRunsCancelProductionRunEndpoint,
  useOpenFarmApiEndpointsProductionRunsCloseProductionRunEndpoint,
  useOpenFarmApiEndpointsProductionRunsGetProductionRunEndpoint,
} from '@/lib/api/generated/production-runs/production-runs';
import { ShortfallBanner } from './components/ShortfallBanner';
import { useProductionHub } from './hooks/useProductionHub';

export function ProductionRunDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  useProductionHub();

  const [confirmCancelOpen, setConfirmCancelOpen] = useState(false);

  const runQuery =
    useOpenFarmApiEndpointsProductionRunsGetProductionRunEndpoint(id ?? '');
  const run = runQuery.data?.data;

  function invalidateRunQueries() {
    queryClient.invalidateQueries({
      queryKey:
        getOpenFarmApiEndpointsProductionRunsListProductionRunsEndpointQueryKey(),
    });
    if (id) {
      queryClient.invalidateQueries({
        queryKey:
          getOpenFarmApiEndpointsProductionRunsGetProductionRunEndpointQueryKey(
            id,
          ),
      });
    }
  }

  const closeMutation =
    useOpenFarmApiEndpointsProductionRunsCloseProductionRunEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Run closed.');
          invalidateRunQueries();
        },
        onError: async (error) => {
          toast.error(
            await extractErrorDetail(error, 'Could not close this run.'),
          );
        },
      },
    });

  const cancelMutation =
    useOpenFarmApiEndpointsProductionRunsCancelProductionRunEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Run cancelled.');
          invalidateRunQueries();
        },
        onError: async (error) => {
          toast.error(
            await extractErrorDetail(error, 'Could not cancel this run.'),
          );
        },
      },
    });

  if (runQuery.isLoading) {
    return (
      <div className="flex flex-col gap-6">
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  if (!run) {
    return (
      <EmptyState
        title="Production run not found"
        description="It may have been removed, or the link is out of date."
        action={
          <Button
            variant="outline"
            onClick={() => navigate('/production-runs')}
          >
            Back to production runs
          </Button>
        }
      />
    );
  }

  const title =
    run.name ?? run.lines?.[0]?.targetName ?? 'Unnamed production run';

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        above={
          <BackLink to="/production-runs" className="mb-1">
            Back to production runs
          </BackLink>
        }
        title={title}
        description={
          <span className="flex items-center gap-2">
            <Badge variant="outline">{run.status}</Badge>
            {run.dueDate && <span>Due {run.dueDate}</span>}
          </span>
        }
        action={
          <div className="flex gap-2">
            {run.status === 'Complete' && (
              <Button
                variant="outline"
                onClick={() => id && closeMutation.mutate({ id })}
                disabled={closeMutation.isPending}
              >
                <CheckIcon /> Close run
              </Button>
            )}
            {(run.status === 'Active' || run.status === 'Complete') && (
              <Button
                variant="destructive"
                onClick={() => setConfirmCancelOpen(true)}
              >
                <XIcon /> Cancel run
              </Button>
            )}
          </div>
        }
      />

      {run.notes && (
        <p className="text-sm text-muted-foreground">{run.notes}</p>
      )}

      <div className="flex flex-col gap-4">
        {(run.lines ?? []).map((line) => (
          <Card key={line.id}>
            <CardHeader>
              <CardTitle className="flex items-center gap-2 text-base">
                {line.targetName}
                <Badge variant="secondary">{line.targetKind}</Badge>
                <span className="text-xs font-normal text-muted-foreground">
                  {line.quantity === null || line.quantity === undefined
                    ? 'Unlimited (backfill)'
                    : `Qty ${line.quantity}`}{' '}
                  · {line.revisionMode}
                </span>
              </CardTitle>
            </CardHeader>
            <CardContent className="flex flex-col gap-2">
              {(line.requirements ?? []).map((requirement) => (
                <div key={requirement.id} className="flex flex-col gap-1">
                  <div className="flex items-center justify-between rounded-md border p-2">
                    <span className="text-sm font-medium">
                      {requirement.partName}
                    </span>
                    <span className="text-xs text-muted-foreground">
                      {requirement.completedJobCount}/{requirement.jobCount}{' '}
                      completed
                      {requirement.quantity !== null &&
                        requirement.quantity !== undefined &&
                        ` · target ${requirement.quantity}`}
                    </span>
                  </div>
                  {id && (
                    <ShortfallBanner runId={id} requirement={requirement} />
                  )}
                </div>
              ))}
            </CardContent>
          </Card>
        ))}
      </div>

      <ConfirmDialog
        open={confirmCancelOpen}
        onOpenChange={setConfirmCancelOpen}
        title="Cancel this production run?"
        description="Every queued, undispatched job on this run will be cancelled. A job already printing is left alone and still records its outcome."
        confirmLabel="Cancel run"
        onConfirm={() => {
          if (id) cancelMutation.mutate({ id });
          setConfirmCancelOpen(false);
        }}
      />
    </div>
  );
}
