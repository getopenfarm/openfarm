import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
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
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type { ListNonPrintedComponentsResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsNonPrintedComponentsListNonPrintedComponentsEndpointQueryKey,
  useOpenFarmApiEndpointsNonPrintedComponentsCreateNonPrintedComponentEndpoint,
  useOpenFarmApiEndpointsNonPrintedComponentsDeleteNonPrintedComponentEndpoint,
  useOpenFarmApiEndpointsNonPrintedComponentsEditNonPrintedComponentEndpoint,
  useOpenFarmApiEndpointsNonPrintedComponentsListNonPrintedComponentsEndpoint,
} from '@/lib/api/generated/non-printed-components/non-printed-components';

const schema = z.object({
  name: z.string().min(1, 'Name is required'),
  unit: z.string().min(1, 'Unit is required'),
  unitCost: z.string().optional(),
});

type FormValues = z.infer<typeof schema>;

interface NonPrintedComponentFormPageProps {
  mode: 'add' | 'edit';
}

export function NonPrintedComponentFormPage({
  mode,
}: NonPrintedComponentFormPageProps) {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const componentsQuery =
    useOpenFarmApiEndpointsNonPrintedComponentsListNonPrintedComponentsEndpoint(
      { page: 1, pageSize: 500 },
      { query: { enabled: mode === 'edit' } },
    );
  const existing = (
    (componentsQuery.data?.data as ListNonPrintedComponentsResponse | undefined)
      ?.items ?? []
  ).find((c) => c.id === id);

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: '', unit: '', unitCost: '' },
  });

  // biome-ignore lint/correctness/useExhaustiveDependencies: only re-seeds when the fetched component's identity changes, not on every render.
  useEffect(() => {
    if (mode === 'edit' && existing) {
      form.reset({
        name: existing.name ?? '',
        unit: existing.unit ?? '',
        unitCost: existing.unitCost != null ? String(existing.unitCost) : '',
      });
    }
  }, [existing?.id]);

  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(
    form.formState.isDirty,
  );

  const [confirmDeleteOpen, setConfirmDeleteOpen] = useState(false);

  const deleteMutation =
    useOpenFarmApiEndpointsNonPrintedComponentsDeleteNonPrintedComponentEndpoint(
      {
        mutation: {
          onSuccess: () => {
            toast.success('Component deleted.');
            queryClient.invalidateQueries({
              queryKey:
                getOpenFarmApiEndpointsNonPrintedComponentsListNonPrintedComponentsEndpointQueryKey(),
            });
            navigate('/non-printed-components');
          },
          onError: async (error) => {
            toast.error(
              await extractErrorDetail(
                error,
                'Could not delete this component.',
              ),
            );
          },
        },
      },
    );

  const createMutation =
    useOpenFarmApiEndpointsNonPrintedComponentsCreateNonPrintedComponentEndpoint(
      {
        mutation: {
          onSuccess: () => {
            toast.success('Component created.');
            queryClient.invalidateQueries({
              queryKey:
                getOpenFarmApiEndpointsNonPrintedComponentsListNonPrintedComponentsEndpointQueryKey(),
            });
            markClean();
            navigate('/non-printed-components');
          },
          onError: () => toast.error('Could not create this component.'),
        },
      },
    );

  const editMutation =
    useOpenFarmApiEndpointsNonPrintedComponentsEditNonPrintedComponentEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Component updated.');
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsNonPrintedComponentsListNonPrintedComponentsEndpointQueryKey(),
          });
          markClean();
          navigate('/non-printed-components');
        },
        onError: () => toast.error('Could not update this component.'),
      },
    });

  if (mode === 'edit' && componentsQuery.isLoading) {
    return (
      <div className="flex flex-col gap-6">
        <Skeleton className="h-48 w-full" />
      </div>
    );
  }

  if (mode === 'edit' && !existing) {
    return (
      <EmptyState
        title="Component not found"
        description="It may have been removed, or the link is out of date."
        action={
          <Button
            variant="outline"
            onClick={() => navigate('/non-printed-components')}
          >
            Back to non-printed components
          </Button>
        }
      />
    );
  }

  function onSubmit(values: FormValues) {
    const unitCost =
      values.unitCost && values.unitCost.trim().length > 0
        ? Number(values.unitCost)
        : null;

    if (mode === 'add') {
      createMutation.mutate({
        data: { name: values.name.trim(), unit: values.unit.trim(), unitCost },
      });
    } else if (id) {
      editMutation.mutate({
        id,
        data: { name: values.name.trim(), unit: values.unit.trim(), unitCost },
      });
    }
  }

  const isSaving = createMutation.isPending || editMutation.isPending;

  return (
    <div className="flex flex-col gap-6 pb-4">
      <PageHeader
        above={
          <BackLink to="/non-printed-components" className="mb-1">
            Back to non-printed components
          </BackLink>
        }
        title={mode === 'add' ? 'New non-printed component' : 'Edit component'}
        description="Screws, inserts, packaging, and other reusable BOM components that aren't printed."
        action={
          mode === 'edit' ? (
            <Button
              variant="destructive"
              type="button"
              onClick={() => setConfirmDeleteOpen(true)}
            >
              Delete
            </Button>
          ) : undefined
        }
      />

      <Form {...form}>
        <form
          onSubmit={form.handleSubmit(onSubmit)}
          className="flex flex-col gap-6"
        >
          <Card>
            <CardContent className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <FormField
                control={form.control}
                name="name"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Name</FormLabel>
                    <FormControl>
                      <Input placeholder="M3x8 screw" {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={form.control}
                name="unit"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Unit</FormLabel>
                    <FormControl>
                      <Input placeholder="each" {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={form.control}
                name="unitCost"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Unit cost (optional)</FormLabel>
                    <FormControl>
                      <Input type="number" step="any" min={0} {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
            </CardContent>
          </Card>

          <FormFooter
            end={
              <>
                <Button
                  type="button"
                  variant="ghost"
                  onClick={() => navigate('/non-printed-components')}
                >
                  Cancel
                </Button>
                <Button type="submit" disabled={isSaving}>
                  {isSaving
                    ? 'Saving…'
                    : mode === 'add'
                      ? 'Create component'
                      : 'Save changes'}
                </Button>
              </>
            }
          />
        </form>
      </Form>

      <ConfirmLeaveDialog />

      <ConfirmDialog
        open={confirmDeleteOpen}
        onOpenChange={setConfirmDeleteOpen}
        title="Delete this component?"
        description="This cannot be undone. Deletion is blocked if a BOM line still references it."
        confirmLabel="Delete"
        onConfirm={() => id && deleteMutation.mutate({ id })}
      />
    </div>
  );
}
