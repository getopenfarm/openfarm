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
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { FieldGroup } from '@/components/ui/field';
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
    defaultValues: { name: '', unitCost: '' },
  });

  // biome-ignore lint/correctness/useExhaustiveDependencies: only re-seeds when the fetched component's identity changes, not on every render.
  useEffect(() => {
    if (mode === 'edit' && existing) {
      form.reset({
        name: existing.name ?? '',
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
            toast.success('Component removed.');
            queryClient.invalidateQueries({
              queryKey:
                getOpenFarmApiEndpointsNonPrintedComponentsListNonPrintedComponentsEndpointQueryKey(),
            });
            navigate('/non-printed-components');
          },
          onError: async (error) => {
            toast.error(
              await extractErrorDetail(error, 'Could not remove component.'),
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
            toast.success('Component added.');
            queryClient.invalidateQueries({
              queryKey:
                getOpenFarmApiEndpointsNonPrintedComponentsListNonPrintedComponentsEndpointQueryKey(),
            });
            markClean();
            navigate('/non-printed-components');
          },
          onError: () => toast.error('Could not add component.'),
        },
      },
    );

  const editMutation =
    useOpenFarmApiEndpointsNonPrintedComponentsEditNonPrintedComponentEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Component saved.');
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsNonPrintedComponentsListNonPrintedComponentsEndpointQueryKey(),
          });
          markClean();
          navigate('/non-printed-components');
        },
        onError: () => toast.error('Could not save component.'),
      },
    });

  const backLink = (
    <BackLink to="/non-printed-components" className="mb-1">
      Back to non-printed components
    </BackLink>
  );

  if (mode === 'edit' && !existing) {
    return (
      <Page>
        <PageHeader above={backLink} title="Edit component" />
        {componentsQuery.isLoading ? (
          <Skeleton className="h-48 w-full" />
        ) : (
          <EmptyState
            title="Component not found"
            description="Removed or invalid link."
            action={
              <Button
                variant="outline"
                onClick={() => navigate('/non-printed-components')}
              >
                Back to non-printed components
              </Button>
            }
          />
        )}
      </Page>
    );
  }

  function onSubmit(values: FormValues) {
    const unitCost =
      values.unitCost && values.unitCost.trim().length > 0
        ? Number(values.unitCost)
        : null;

    if (mode === 'add') {
      createMutation.mutate({
        data: { name: values.name.trim(), unitCost },
      });
    } else if (id) {
      editMutation.mutate({
        id,
        data: { name: values.name.trim(), unitCost },
      });
    }
  }

  const isSaving = createMutation.isPending || editMutation.isPending;

  return (
    <Page className="flex-1">
      <PageHeader
        above={backLink}
        title={mode === 'add' ? 'Add component' : 'Edit component'}
        description="Screws, inserts, packaging, and other reusable BOM components that are not printed."
        action={
          mode === 'edit' ? (
            <Button
              variant="destructive"
              type="button"
              onClick={() => setConfirmDeleteOpen(true)}
            >
              Remove
            </Button>
          ) : undefined
        }
      />

      <Form {...form}>
        <form
          onSubmit={form.handleSubmit(onSubmit)}
          className="flex flex-1 flex-col gap-6"
        >
          <FieldGroup className="flex-1 gap-6">
            <Card>
              <CardHeader>
                <CardTitle>Details</CardTitle>
              </CardHeader>
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
                  name="unitCost"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>Cost per piece (optional)</FormLabel>
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
                    variant="outline"
                    onClick={() => navigate('/non-printed-components')}
                  >
                    Cancel
                  </Button>
                  <Button type="submit" disabled={isSaving}>
                    {isSaving && <Spinner data-icon="inline-start" />}
                    {mode === 'add'
                      ? isSaving
                        ? 'Adding…'
                        : 'Add component'
                      : isSaving
                        ? 'Saving…'
                        : 'Save'}
                  </Button>
                </>
              }
            />
          </FieldGroup>
        </form>
      </Form>

      <ConfirmLeaveDialog />

      <ConfirmDialog
        open={confirmDeleteOpen}
        onOpenChange={setConfirmDeleteOpen}
        title="Remove component?"
        description="This cannot be undone. Removal is blocked while a BOM line references it."
        confirmLabel="Remove"
        destructive
        isPending={deleteMutation.isPending}
        onConfirm={() => id && deleteMutation.mutate({ id })}
      />
    </Page>
  );
}
