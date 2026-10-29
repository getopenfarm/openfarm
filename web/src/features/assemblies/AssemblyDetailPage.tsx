import { useQueryClient } from '@tanstack/react-query';
import { ListPlusIcon, TrashIcon } from 'lucide-react';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { BackLink } from '@/components/back-link/BackLink';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { Spinner } from '@/components/ui/spinner';
import { SendToQueueDialog } from '@/features/production-runs/components/SendToQueueDialog';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import {
  getOpenFarmApiEndpointsAssembliesExplodeAssemblyEndpointQueryKey,
  getOpenFarmApiEndpointsAssembliesGetAssemblyEndpointQueryKey,
  getOpenFarmApiEndpointsAssembliesListAssembliesEndpointQueryKey,
  useOpenFarmApiEndpointsAssembliesDeleteAssemblyEndpoint,
  useOpenFarmApiEndpointsAssembliesEditAssemblyEndpoint,
  useOpenFarmApiEndpointsAssembliesGetAssemblyEndpoint,
} from '@/lib/api/generated/assemblies/assemblies';
import type { AssemblyResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  AssemblyEditor,
  type AssemblyFormValues,
  toLinePayload,
  toPricingPayload,
  useAssemblyForm,
} from './components/AssemblyEditor';

export function AssemblyDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [confirmDeleteOpen, setConfirmDeleteOpen] = useState(false);
  const [queueOpen, setQueueOpen] = useState(false);

  const assemblyQuery = useOpenFarmApiEndpointsAssembliesGetAssemblyEndpoint(
    id ?? '',
  );
  const assembly = assemblyQuery.data?.data as AssemblyResponse | undefined;

  const { form, resetTo } = useAssemblyForm(assembly);
  const { isDirty } = form.formState;
  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(isDirty);

  const invalidateList = () =>
    queryClient.invalidateQueries({
      queryKey:
        getOpenFarmApiEndpointsAssembliesListAssembliesEndpointQueryKey(),
    });

  const editMutation = useOpenFarmApiEndpointsAssembliesEditAssemblyEndpoint({
    mutation: {
      onSuccess: (response) => {
        if (response.data) resetTo(response.data);
        void queryClient.invalidateQueries({
          predicate: (query) =>
            String(query.queryKey[0]).startsWith('/api/v1/assemblies'),
        });
        invalidateList();
        if (id) {
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsAssembliesGetAssemblyEndpointQueryKey(id),
          });
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsAssembliesExplodeAssemblyEndpointQueryKey(
                id,
              ),
          });
        }
        toast.success('Assembly saved.');
      },
      onError: async (error) => {
        toast.error(
          await extractErrorDetail(error, 'Could not save assembly.'),
        );
      },
    },
  });

  const deleteMutation =
    useOpenFarmApiEndpointsAssembliesDeleteAssemblyEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Assembly removed.');
          invalidateList();
          markClean();
          navigate('/assemblies');
        },
        onError: async (error) => {
          toast.error(
            await extractErrorDetail(error, 'Could not remove assembly.'),
          );
        },
      },
    });

  if (!assembly) {
    const loadFailed =
      assemblyQuery.isError &&
      (assemblyQuery.error as unknown as { status?: number } | null)?.status !==
        404;
    return (
      <Page>
        <PageHeader
          above={
            <BackLink to="/assemblies" className="mb-1">
              Back to assemblies
            </BackLink>
          }
          title="Assembly"
        />
        {assemblyQuery.isLoading ? (
          <Skeleton className="h-64 w-full" />
        ) : loadFailed ? (
          <ErrorState
            title="Could not load assembly"
            description="Try again."
            onRetry={() => assemblyQuery.refetch()}
          />
        ) : (
          <EmptyState
            title="Assembly not found"
            description="Removed or invalid link."
            action={
              <Button variant="outline" onClick={() => navigate('/assemblies')}>
                Back to assemblies
              </Button>
            }
          />
        )}
      </Page>
    );
  }

  function onSubmit(values: AssemblyFormValues) {
    if (!id) return;
    editMutation.mutate({
      id,
      data: {
        additionalCosts: values.additionalCosts,
        pricing: toPricingPayload(values.pricing),
        name: values.name.trim(),
        description: values.description?.trim() || null,
        lines: values.lines.map((l) => ({
          lineId: l.lineId ?? null,
          ...toLinePayload(l),
        })),
      },
    });
  }

  const pending = editMutation.isPending;

  return (
    <Page className="flex-1">
      <AssemblyEditor
        form={form}
        onSubmit={onSubmit}
        assemblyId={id}
        headerAction={
          <div className="flex shrink-0 flex-wrap gap-2">
            <Button
              type="button"
              variant="outline"
              disabled={isDirty}
              title={isDirty ? 'Save changes before queueing.' : undefined}
              onClick={() => setQueueOpen(true)}
            >
              <ListPlusIcon data-icon="inline-start" /> Send to queue
            </Button>
            <Button
              type="button"
              variant="destructive"
              onClick={() => setConfirmDeleteOpen(true)}
            >
              <TrashIcon data-icon="inline-start" /> Remove
            </Button>
          </div>
        }
        footerActions={
          <>
            <Button
              type="button"
              variant="outline"
              onClick={() => navigate('/assemblies')}
            >
              Cancel
            </Button>
            <Button type="submit" disabled={!isDirty || pending}>
              {pending && <Spinner data-icon="inline-start" />}
              {pending ? 'Saving…' : 'Save'}
            </Button>
          </>
        }
      />

      <ConfirmDialog
        open={confirmDeleteOpen}
        onOpenChange={setConfirmDeleteOpen}
        title="Remove assembly?"
        description="This cannot be undone. Removal is blocked while another assembly references it."
        confirmLabel="Remove"
        destructive
        isPending={deleteMutation.isPending}
        onConfirm={() => id && deleteMutation.mutate({ id })}
      />
      {queueOpen && (
        <SendToQueueDialog
          target={{ kind: 'Assembly', id: id ?? '', name: assembly.name ?? '' }}
          onClose={() => setQueueOpen(false)}
        />
      )}
      <ConfirmLeaveDialog />
    </Page>
  );
}
