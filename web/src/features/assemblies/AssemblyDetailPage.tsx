import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { TrashIcon } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '@/components/ui/form';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { Spinner } from '@/components/ui/spinner';
import { Textarea } from '@/components/ui/textarea';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import {
  getOpenFarmApiEndpointsAssembliesGetAssemblyEndpointQueryKey,
  getOpenFarmApiEndpointsAssembliesListAssembliesEndpointQueryKey,
  useOpenFarmApiEndpointsAssembliesDeleteAssemblyEndpoint,
  useOpenFarmApiEndpointsAssembliesEditAssemblyEndpoint,
  useOpenFarmApiEndpointsAssembliesGetAssemblyEndpoint,
} from '@/lib/api/generated/assemblies/assemblies';
import type { AssemblyResponse } from '@/lib/api/generated/endpoints.schemas';
import { BomLineEditor, type EditableLine } from './components/BomLineEditor';
import { BuildableUnitsPanel } from './components/BuildableUnitsPanel';
import { ExplosionResultView } from './components/ExplosionResultView';

const schema = z.object({
  name: z.string().min(1, 'Give this assembly a name first.'),
  description: z.string().optional(),
  lines: z
    .array(z.custom<EditableLine>())
    .min(1, 'An assembly needs at least one line.'),
});

type FormValues = z.infer<typeof schema>;

function toFormValues(assembly: AssemblyResponse): FormValues {
  return {
    name: assembly.name ?? '',
    description: assembly.description ?? '',
    lines: (assembly.lines ?? []).map((line) => ({
      key: line.id ?? crypto.randomUUID(),
      lineId: line.id,
      targetKind: (line.targetKind ?? 'Part') as EditableLine['targetKind'],
      targetId: line.targetId ?? '',
      targetName: line.targetName ?? '',
      quantity: line.quantity ?? 1,
    })),
  };
}

export function AssemblyDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [confirmDeleteOpen, setConfirmDeleteOpen] = useState(false);

  const assemblyQuery = useOpenFarmApiEndpointsAssembliesGetAssemblyEndpoint(
    id ?? '',
  );
  const assembly = assemblyQuery.data?.data;

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: '', description: '', lines: [] },
  });
  const { isDirty } = form.formState;

  // biome-ignore lint/correctness/useExhaustiveDependencies: only re-seeds when the fetched assembly's identity changes, not on every refetch (which would clobber the draft).
  useEffect(() => {
    if (assembly) form.reset(toFormValues(assembly));
  }, [assembly?.id]);

  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(isDirty);

  const invalidateList = () =>
    queryClient.invalidateQueries({
      queryKey:
        getOpenFarmApiEndpointsAssembliesListAssembliesEndpointQueryKey(),
    });

  const editMutation = useOpenFarmApiEndpointsAssembliesEditAssemblyEndpoint({
    mutation: {
      onSuccess: (response) => {
        if (response.data) form.reset(toFormValues(response.data));
        invalidateList();
        if (id)
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsAssembliesGetAssemblyEndpointQueryKey(id),
          });
        toast.success('Saved.');
      },
      onError: async (error) => {
        toast.error(
          await extractErrorDetail(error, 'Could not save this assembly.'),
        );
      },
    },
  });

  const deleteMutation =
    useOpenFarmApiEndpointsAssembliesDeleteAssemblyEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Assembly deleted.');
          invalidateList();
          markClean();
          navigate('/assemblies');
        },
        onError: async (error) => {
          toast.error(
            await extractErrorDetail(error, 'Could not delete this assembly.'),
          );
        },
      },
    });

  const backLink = (
    <BackLink to="/assemblies" className="mb-1">
      Back to assemblies
    </BackLink>
  );

  if (!assembly) {
    const loadFailed =
      assemblyQuery.isError &&
      (assemblyQuery.error as unknown as { status?: number } | null)?.status !==
        404;
    return (
      <Page>
        <PageHeader above={backLink} title="Assembly" />
        {assemblyQuery.isLoading ? (
          <Skeleton className="h-64 w-full" />
        ) : loadFailed ? (
          <ErrorState
            title="Could not load this assembly"
            description="Please try again."
            onRetry={() => assemblyQuery.refetch()}
          />
        ) : (
          <EmptyState
            title="Assembly not found"
            description="It may have been removed, or the link is out of date."
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

  function onSubmit(values: FormValues) {
    if (!id) return;
    editMutation.mutate({
      id,
      data: {
        name: values.name.trim(),
        description: values.description?.trim() || null,
        lines: values.lines.map((l) => ({
          lineId: l.lineId ?? null,
          targetKind: l.targetKind,
          targetId: l.targetId,
          quantity: l.quantity,
        })),
      },
    });
  }

  const pending = editMutation.isPending;

  return (
    <Page className="flex-1">
      <PageHeader
        above={backLink}
        title={assembly.name}
        description={assembly.description ?? undefined}
        action={
          <Button
            variant="destructive"
            onClick={() => setConfirmDeleteOpen(true)}
          >
            <TrashIcon data-icon="inline-start" /> Delete
          </Button>
        }
      />

      <Form {...form}>
        <form
          id="assembly-form"
          onSubmit={form.handleSubmit(onSubmit)}
          className="flex flex-col gap-6"
        >
          <Card>
            <CardHeader>
              <CardTitle>Details</CardTitle>
            </CardHeader>
            <CardContent className="flex flex-col gap-4">
              <FormField
                control={form.control}
                name="name"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Assembly name</FormLabel>
                    <FormControl>
                      <Input {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={form.control}
                name="description"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Description</FormLabel>
                    <FormControl>
                      <Textarea rows={2} {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
            </CardContent>
          </Card>

          <FormField
            control={form.control}
            name="lines"
            render={({ field }) => (
              <Card>
                <CardHeader>
                  <CardTitle>Lines · {field.value.length}</CardTitle>
                </CardHeader>
                <CardContent className="flex flex-col gap-2">
                  <BomLineEditor
                    lines={field.value}
                    onChange={field.onChange}
                    excludeAssemblyId={id}
                  />
                  <FormMessage />
                </CardContent>
              </Card>
            )}
          />
        </form>
      </Form>

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

      <FormFooter
        end={
          <>
            <Button
              type="button"
              variant="outline"
              onClick={() => navigate('/assemblies')}
            >
              Cancel
            </Button>
            <Button
              type="submit"
              form="assembly-form"
              disabled={!isDirty || pending}
            >
              {pending && <Spinner data-icon="inline-start" />}
              {pending ? 'Saving…' : 'Save'}
            </Button>
          </>
        }
      />

      <ConfirmDialog
        open={confirmDeleteOpen}
        onOpenChange={setConfirmDeleteOpen}
        title="Delete this assembly?"
        description="This cannot be undone. Deletion is blocked if another assembly still references it."
        confirmLabel="Delete"
        destructive
        isPending={deleteMutation.isPending}
        onConfirm={() => id && deleteMutation.mutate({ id })}
      />
      <ConfirmLeaveDialog />
    </Page>
  );
}
