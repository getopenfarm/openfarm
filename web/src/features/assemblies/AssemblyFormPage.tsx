import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { FormFooter } from '@/components/form-footer/FormFooter';
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
import { Textarea } from '@/components/ui/textarea';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import {
  getOpenFarmApiEndpointsAssembliesGetAssemblyEndpointQueryKey,
  getOpenFarmApiEndpointsAssembliesListAssembliesEndpointQueryKey,
  useOpenFarmApiEndpointsAssembliesAddBomLineEndpoint,
  useOpenFarmApiEndpointsAssembliesCreateAssemblyEndpoint,
  useOpenFarmApiEndpointsAssembliesEditAssemblyEndpoint,
  useOpenFarmApiEndpointsAssembliesEditBomLineEndpoint,
  useOpenFarmApiEndpointsAssembliesGetAssemblyEndpoint,
  useOpenFarmApiEndpointsAssembliesRemoveBomLineEndpoint,
} from '@/lib/api/generated/assemblies/assemblies';
import type { AssemblyResponse } from '@/lib/api/generated/endpoints.schemas';
import { BomLineEditor, type EditableLine } from './components/BomLineEditor';

const schema = z.object({
  name: z.string().min(1, 'Give this assembly a name first.'),
  description: z.string().optional(),
});

type FormValues = z.infer<typeof schema>;

interface AssemblyFormPageProps {
  mode: 'add' | 'edit';
}

function toEditableLines(
  assembly: AssemblyResponse | undefined,
): EditableLine[] {
  return (assembly?.lines ?? []).map((line) => ({
    key: line.id ?? crypto.randomUUID(),
    lineId: line.id,
    targetKind: (line.targetKind ?? 'Part') as EditableLine['targetKind'],
    targetId: line.targetId ?? '',
    targetName: line.targetName ?? '',
    quantity: line.quantity ?? 1,
  }));
}

export function AssemblyFormPage({ mode }: AssemblyFormPageProps) {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const assemblyQuery = useOpenFarmApiEndpointsAssembliesGetAssemblyEndpoint(
    id ?? '',
    { query: { enabled: mode === 'edit' && !!id } },
  );
  const existing = assemblyQuery.data?.data as AssemblyResponse | undefined;

  const [lines, setLines] = useState<EditableLine[]>([]);
  const [originalLines, setOriginalLines] = useState<EditableLine[]>([]);
  const [isSaving, setIsSaving] = useState(false);

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: '', description: '' },
  });

  // biome-ignore lint/correctness/useExhaustiveDependencies: only re-seeds when the fetched assembly's identity changes, not on every render.
  useEffect(() => {
    if (mode === 'edit' && existing) {
      form.reset({
        name: existing.name ?? '',
        description: existing.description ?? '',
      });
      const seeded = toEditableLines(existing);
      setLines(seeded);
      setOriginalLines(seeded);
    }
  }, [existing?.id]);

  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(
    form.formState.isDirty || lines !== originalLines,
  );

  const createMutation =
    useOpenFarmApiEndpointsAssembliesCreateAssemblyEndpoint();
  const editAssemblyMutation =
    useOpenFarmApiEndpointsAssembliesEditAssemblyEndpoint();
  const addLineMutation = useOpenFarmApiEndpointsAssembliesAddBomLineEndpoint();
  const editLineMutation =
    useOpenFarmApiEndpointsAssembliesEditBomLineEndpoint();
  const removeLineMutation =
    useOpenFarmApiEndpointsAssembliesRemoveBomLineEndpoint();

  if (mode === 'edit' && assemblyQuery.isLoading) {
    return (
      <div className="flex flex-col gap-6">
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  if (mode === 'edit' && !existing) {
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

  async function onSubmit(values: FormValues) {
    setIsSaving(true);
    try {
      if (mode === 'add') {
        if (lines.length === 0) {
          toast.error('Add at least one line first.');
          return;
        }
        const created = await createMutation.mutateAsync({
          data: {
            name: values.name.trim(),
            description: values.description?.trim() || null,
            lines: lines.map((l) => ({
              targetKind: l.targetKind,
              targetId: l.targetId,
              quantity: l.quantity,
            })),
          },
        });
        toast.success('Assembly created.');
        queryClient.invalidateQueries({
          queryKey:
            getOpenFarmApiEndpointsAssembliesListAssembliesEndpointQueryKey(),
        });
        markClean();
        navigate(`/assemblies/${created.data?.id}`);
        return;
      }

      // Edit mode: rename if changed, then flush queued line adds/edits/removes sequentially
      // (contracts/assembly-bom-api.md's "Client UX note" — one operator-perceived save, one
      // API call per logical line change).
      if (!id) return;

      if (
        values.name.trim() !== existing?.name ||
        (values.description?.trim() || null) !== (existing?.description ?? null)
      ) {
        await editAssemblyMutation.mutateAsync({
          id,
          data: {
            name: values.name.trim(),
            description: values.description?.trim() || null,
          },
        });
      }

      for (const removed of originalLines) {
        if (!removed.lineId) continue;
        if (lines.some((l) => l.lineId === removed.lineId)) continue;

        await removeLineMutation.mutateAsync({
          id,
          lineId: removed.lineId,
          data: {},
        });
      }

      for (const line of lines) {
        if (!line.lineId) {
          await addLineMutation.mutateAsync({
            id,
            data: {
              targetKind: line.targetKind,
              targetId: line.targetId,
              quantity: line.quantity,
            },
          });
          continue;
        }

        const original = originalLines.find((o) => o.lineId === line.lineId);
        if (original && original.quantity !== line.quantity) {
          await editLineMutation.mutateAsync({
            id,
            lineId: line.lineId,
            data: { quantity: line.quantity },
          });
        }
      }

      toast.success('Assembly updated.');
      queryClient.invalidateQueries({
        queryKey:
          getOpenFarmApiEndpointsAssembliesListAssembliesEndpointQueryKey(),
      });
      queryClient.invalidateQueries({
        queryKey:
          getOpenFarmApiEndpointsAssembliesGetAssemblyEndpointQueryKey(id),
      });
      markClean();
      navigate(`/assemblies/${id}`);
    } catch (error) {
      const detail = await extractErrorDetail(
        error,
        'Could not save this assembly.',
      );
      toast.error(detail);
    } finally {
      setIsSaving(false);
    }
  }

  return (
    <div className="flex flex-col gap-6 pb-4">
      <PageHeader
        above={
          <BackLink to="/assemblies" className="mb-1">
            Back to assemblies
          </BackLink>
        }
        title={mode === 'add' ? 'New assembly' : 'Edit assembly'}
        description={
          mode === 'add'
            ? 'Group parts, sub-assemblies, and non-printed components into a reusable bill of materials.'
            : "Update this assembly's details. Line changes save immediately alongside the rename."
        }
      />

      <Form {...form}>
        <form
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

          <Card>
            <CardHeader>
              <CardTitle>Lines · {lines.length}</CardTitle>
            </CardHeader>
            <CardContent>
              <BomLineEditor
                lines={lines}
                onChange={setLines}
                excludeAssemblyId={id}
              />
            </CardContent>
          </Card>

          <FormFooter
            end={
              <>
                <Button
                  type="button"
                  variant="ghost"
                  onClick={() => navigate('/assemblies')}
                >
                  Cancel
                </Button>
                <Button type="submit" disabled={isSaving}>
                  {isSaving
                    ? 'Saving…'
                    : mode === 'add'
                      ? 'Create assembly'
                      : 'Save changes'}
                </Button>
              </>
            }
          />
        </form>
      </Form>

      <ConfirmLeaveDialog />
    </div>
  );
}
