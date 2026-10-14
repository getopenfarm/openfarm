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
  getOpenFarmApiEndpointsAssembliesListAssembliesEndpointQueryKey,
  useOpenFarmApiEndpointsAssembliesCreateAssemblyEndpoint,
} from '@/lib/api/generated/assemblies/assemblies';
import { BomLineEditor, type EditableLine } from './components/BomLineEditor';

const schema = z.object({
  name: z.string().min(1, 'Give this assembly a name first.'),
  description: z.string().optional(),
});

type FormValues = z.infer<typeof schema>;

export function AssemblyFormPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [lines, setLines] = useState<EditableLine[]>([]);
  const [isSaving, setIsSaving] = useState(false);

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: '', description: '' },
  });

  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(
    form.formState.isDirty || lines.length > 0,
  );

  const createMutation =
    useOpenFarmApiEndpointsAssembliesCreateAssemblyEndpoint();

  const backLink = (
    <BackLink to="/assemblies" className="mb-1">
      Back to assemblies
    </BackLink>
  );

  async function onSubmit(values: FormValues) {
    setIsSaving(true);
    try {
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
    <Page className="flex-1">
      <PageHeader
        above={backLink}
        title="Add assembly"
        description="Group parts, sub-assemblies, and non-printed components into a reusable bill of materials."
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
                <BomLineEditor lines={lines} onChange={setLines} />
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
                  <Button type="submit" disabled={isSaving}>
                    {isSaving && <Spinner data-icon="inline-start" />}
                    {isSaving ? 'Saving…' : 'Save'}
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
