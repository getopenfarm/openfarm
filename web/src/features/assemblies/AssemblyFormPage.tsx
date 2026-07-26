import { zodResolver } from '@hookform/resolvers/zod';
import { PlusIcon } from 'lucide-react';
import { useState } from 'react';
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
import { Textarea } from '@/components/ui/textarea';
import { partsStore } from '@/features/assemblies/data/mockPartsStore';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { ComponentRow } from './components/ComponentRow';
import {
  PartPickerDialog,
  type PartPickerResult,
} from './components/PartPickerDialog';
import { SummarySidebar } from './components/SummarySidebar';
import {
  type Assembly,
  type AssemblyComponent,
  assembliesStore,
  useAssemblies,
} from './data/mockAssembliesStore';

const schema = z.object({
  name: z.string().min(1, 'Give this assembly a name first.'),
  description: z.string().optional(),
  components: z.array(
    z.object({
      id: z.string(),
      partId: z.string().optional(),
      subAssemblyId: z.string().optional(),
      quantity: z.number(),
    }),
  ),
});

type FormValues = z.infer<typeof schema>;

interface AssemblyFormPageProps {
  mode: 'add' | 'edit';
}

export function AssemblyFormPage({ mode }: AssemblyFormPageProps) {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [assemblies] = useAssemblies();
  const parts = partsStore.getSnapshot();

  const existing =
    mode === 'edit' ? assemblies.find((a) => a.id === id) : undefined;

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: existing?.name ?? '',
      description: existing?.description ?? '',
      components: existing?.components ?? [],
    },
  });

  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(
    form.formState.isDirty,
  );

  const [pickerOpen, setPickerOpen] = useState(false);
  const [editingComponentId, setEditingComponentId] = useState<string | null>(
    null,
  );

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

  const components = form.watch('components');
  const editingComponent = editingComponentId
    ? (components.find((c) => c.id === editingComponentId) ?? null)
    : null;

  function openAddPicker() {
    setEditingComponentId(null);
    setPickerOpen(true);
  }

  function openEditPicker(componentId: string) {
    setEditingComponentId(componentId);
    setPickerOpen(true);
  }

  function handlePickerSubmit({ partId, quantity }: PartPickerResult) {
    if (editingComponentId) {
      form.setValue(
        'components',
        components.map((c) =>
          c.id === editingComponentId ? { ...c, partId, quantity } : c,
        ),
        { shouldDirty: true },
      );
    } else {
      form.setValue(
        'components',
        [
          ...components,
          { id: `component-${crypto.randomUUID()}`, partId, quantity },
        ],
        { shouldDirty: true },
      );
    }
    setPickerOpen(false);
    setEditingComponentId(null);
  }

  function removeComponent(componentId: string) {
    form.setValue(
      'components',
      components.filter((c) => c.id !== componentId),
      { shouldDirty: true },
    );
  }

  function onSubmit(values: FormValues) {
    const savedAssembly: Assembly = {
      id: existing?.id ?? `assembly-${crypto.randomUUID()}`,
      name: values.name.trim(),
      description: (values.description ?? '').trim(),
      components: values.components as AssemblyComponent[],
    };

    if (mode === 'add') {
      assembliesStore.setState((prev) => [...prev, savedAssembly]);
      toast.success('Assembly created.');
    } else {
      assembliesStore.setState((prev) =>
        prev.map((a) => (a.id === savedAssembly.id ? savedAssembly : a)),
      );
      toast.success('Assembly updated.');
    }
    markClean();
    navigate('/assemblies');
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
            ? 'Group parts (and other assemblies) into a reusable bill of materials.'
            : "Update this assembly's parts and details."
        }
      />

      <Form {...form}>
        <form
          onSubmit={form.handleSubmit(onSubmit)}
          className="flex flex-col gap-6"
        >
          <div className="grid grid-cols-1 gap-6 lg:grid-cols-[1fr_18rem]">
            <div className="flex flex-col gap-6">
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

              <Card>
                <CardHeader className="flex-row items-center justify-between">
                  <CardTitle>Parts · {components.length}</CardTitle>
                  <Button type="button" size="sm" onClick={openAddPicker}>
                    <PlusIcon /> Add part
                  </Button>
                </CardHeader>
                <CardContent className="flex flex-col gap-2">
                  {components.length === 0 ? (
                    <EmptyState
                      size="sm"
                      title="No parts yet"
                      description='Use "Add part" to build the bill of materials.'
                    />
                  ) : (
                    components.map((component) => (
                      <ComponentRow
                        key={component.id}
                        component={component as AssemblyComponent}
                        parts={parts}
                        allAssemblies={assemblies}
                        onEdit={() => openEditPicker(component.id)}
                        onRemove={() => removeComponent(component.id)}
                      />
                    ))
                  )}
                </CardContent>
              </Card>
            </div>

            <SummarySidebar
              components={components as AssemblyComponent[]}
              allAssemblies={assemblies}
            />
          </div>

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
                <Button type="submit">
                  {mode === 'add' ? 'Create assembly' : 'Save changes'}
                </Button>
              </>
            }
          />
        </form>
      </Form>

      <PartPickerDialog
        open={pickerOpen}
        onOpenChange={setPickerOpen}
        mode={editingComponent ? 'edit' : 'add'}
        initialPartId={editingComponent?.partId}
        initialQuantity={editingComponent?.quantity}
        onSubmit={handlePickerSubmit}
      />

      <ConfirmLeaveDialog />
    </div>
  );
}
