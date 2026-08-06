import { useQueryClient } from '@tanstack/react-query';
import { PencilIcon, TrashIcon } from 'lucide-react';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { BackLink } from '@/components/back-link/BackLink';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import {
  getOpenFarmApiEndpointsAssembliesListAssembliesEndpointQueryKey,
  useOpenFarmApiEndpointsAssembliesDeleteAssemblyEndpoint,
  useOpenFarmApiEndpointsAssembliesGetAssemblyEndpoint,
} from '@/lib/api/generated/assemblies/assemblies';
import { AssemblyLineRow } from './components/AssemblyLineRow';
import { BuildableUnitsPanel } from './components/BuildableUnitsPanel';
import { ExplosionResultView } from './components/ExplosionResultView';

export function AssemblyDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [confirmDeleteOpen, setConfirmDeleteOpen] = useState(false);

  const assemblyQuery = useOpenFarmApiEndpointsAssembliesGetAssemblyEndpoint(
    id ?? '',
  );
  const assembly = assemblyQuery.data?.data;

  const deleteMutation =
    useOpenFarmApiEndpointsAssembliesDeleteAssemblyEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Assembly deleted.');
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsAssembliesListAssembliesEndpointQueryKey(),
          });
          navigate('/assemblies');
        },
        onError: async (error) => {
          toast.error(
            await extractErrorDetail(error, 'Could not delete this assembly.'),
          );
        },
      },
    });

  if (assemblyQuery.isLoading) {
    return (
      <div className="flex flex-col gap-6">
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  if (!assembly) {
    return (
      <EmptyState
        title="Assembly not found"
        description="It may have been removed, or the link is out of date."
        action={
          <Button variant="outline" onClick={() => navigate('/assemblies')}>
            Back to assemblies
          </Button>
        }
      />
    );
  }

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        above={
          <BackLink to="/assemblies" className="mb-1">
            Back to assemblies
          </BackLink>
        }
        title={assembly.name}
        description={assembly.description ?? undefined}
        action={
          <div className="flex gap-2">
            <Button
              variant="outline"
              onClick={() => navigate(`/assemblies/${id}/edit`)}
            >
              <PencilIcon /> Edit
            </Button>
            <Button
              variant="destructive"
              onClick={() => setConfirmDeleteOpen(true)}
            >
              <TrashIcon /> Delete
            </Button>
          </div>
        }
      />

      <Card>
        <CardHeader>
          <CardTitle>Structure · {assembly.lines?.length ?? 0} lines</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-1">
          {assembly.lines?.length ? (
            assembly.lines.map((line) => (
              <AssemblyLineRow key={line.id} line={line} depth={0} />
            ))
          ) : (
            <p className="text-sm text-muted-foreground">No lines.</p>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Explosion</CardTitle>
        </CardHeader>
        <CardContent>
          {id && <ExplosionResultView assemblyId={id} />}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Buildable units</CardTitle>
        </CardHeader>
        <CardContent>
          {id && <BuildableUnitsPanel assemblyId={id} />}
        </CardContent>
      </Card>

      <ConfirmDialog
        open={confirmDeleteOpen}
        onOpenChange={setConfirmDeleteOpen}
        title="Delete this assembly?"
        description="This cannot be undone. Deletion is blocked if another assembly still references it."
        confirmLabel="Delete"
        onConfirm={() => id && deleteMutation.mutate({ id })}
      />
    </div>
  );
}
