import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
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
import { Spinner } from '@/components/ui/spinner';
import { Textarea } from '@/components/ui/textarea';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import {
  getOpenFarmApiEndpointsProductionRunsListProductionRunsEndpointQueryKey,
  useOpenFarmApiEndpointsProductionRunsCreateProductionRunEndpoint,
} from '@/lib/api/generated/production-runs/production-runs';
import {
  type EditableRunLine,
  RunLineEditor,
} from './components/RunLineEditor';

const schema = z.object({
  name: z.string().optional(),
  dueDate: z.string().optional(),
  priority: z.string().optional(),
  notes: z.string().optional(),
});

type FormValues = z.infer<typeof schema>;

/** Page-first (docs/UI_CONVENTIONS.md), react-hook-form + zod, one or more RunLineEditor rows, single POST /production-runs call — tasks.md T067. */
export function NewProductionRunPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [lines, setLines] = useState<EditableRunLine[]>([]);
  const [isSaving, setIsSaving] = useState(false);

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: '', dueDate: '', priority: '', notes: '' },
  });

  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(
    form.formState.isDirty || lines.length > 0,
  );

  const createMutation =
    useOpenFarmApiEndpointsProductionRunsCreateProductionRunEndpoint();

  async function onSubmit(values: FormValues) {
    if (lines.length === 0) {
      toast.error('Add at least one line.');
      return;
    }

    setIsSaving(true);
    try {
      const created = await createMutation.mutateAsync({
        data: {
          name: values.name?.trim() || null,
          dueDate: values.dueDate?.trim() || null,
          priority: values.priority?.trim() || null,
          notes: values.notes?.trim() || null,
          lines: lines.map((l) => ({
            targetKind: l.targetKind,
            targetId: l.targetId,
            quantity: l.quantity,
            assignmentMode: l.assignmentMode,
            pinnedPrinterId: l.pinnedPrinterId ?? null,
            revisionMode: l.revisionMode,
            ...(l.quantity === null && {
              backfillPrinterCap: l.backfillPrinterCap ?? 1,
              backfillCumulativeUnitCeiling:
                l.backfillCumulativeUnitCeiling ?? null,
            }),
          })),
        },
      });

      toast.success('Production run added.');
      queryClient.invalidateQueries({
        queryKey:
          getOpenFarmApiEndpointsProductionRunsListProductionRunsEndpointQueryKey(),
      });
      markClean();
      navigate(`/production-runs/${created.data?.id}`);
    } catch (error) {
      const detail = await extractErrorDetail(
        error,
        'Could not create production run.',
      );
      toast.error(detail);
    } finally {
      setIsSaving(false);
    }
  }

  return (
    <Page className="flex-1">
      <PageHeader
        above={
          <BackLink to="/production-runs" className="mb-1">
            Back to production runs
          </BackLink>
        }
        title="Add production run"
        description="Queue one or more parts/assemblies together as a single run."
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
              <CardContent className="flex flex-col gap-4">
                <FormField
                  control={form.control}
                  name="name"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>Name (optional)</FormLabel>
                      <FormControl>
                        <Input placeholder="Unnamed run" {...field} />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />

                <FormField
                  control={form.control}
                  name="dueDate"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>Due date (optional)</FormLabel>
                      <FormControl>
                        <Input type="date" {...field} />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />

                <FormField
                  control={form.control}
                  name="priority"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>Priority (optional)</FormLabel>
                      <FormControl>
                        <Input {...field} />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />

                <FormField
                  control={form.control}
                  name="notes"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>Notes (optional)</FormLabel>
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
                <RunLineEditor lines={lines} onChange={setLines} />
              </CardContent>
            </Card>

            <FormFooter
              end={
                <>
                  <Button
                    type="button"
                    variant="outline"
                    onClick={() => navigate('/production-runs')}
                  >
                    Cancel
                  </Button>
                  <Button type="submit" disabled={isSaving}>
                    {isSaving && <Spinner data-icon="inline-start" />}
                    {isSaving ? 'Adding…' : 'Add production run'}
                  </Button>
                </>
              }
            />
          </FieldGroup>
        </form>
      </Form>

      <ConfirmLeaveDialog />
    </Page>
  );
}
