import { PlusIcon } from 'lucide-react';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { BackLink } from '@/components/back-link/BackLink';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { partsStore } from '@/features/assemblies/data/mockPartsStore';
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

  const [name, setName] = useState(existing?.name ?? '');
  const [description, setDescription] = useState(existing?.description ?? '');
  const [components, setComponents] = useState<AssemblyComponent[]>(
    existing?.components ?? [],
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
      setComponents((prev) =>
        prev.map((c) =>
          c.id === editingComponentId ? { ...c, partId, quantity } : c,
        ),
      );
    } else {
      setComponents((prev) => [
        ...prev,
        { id: `component-${crypto.randomUUID()}`, partId, quantity },
      ]);
    }
    setPickerOpen(false);
    setEditingComponentId(null);
  }

  function removeComponent(componentId: string) {
    setComponents((prev) => prev.filter((c) => c.id !== componentId));
  }

  function handleSave() {
    const trimmedName = name.trim();
    if (!trimmedName) {
      toast.error('Give this assembly a name first.');
      return;
    }

    const savedAssembly: Assembly = {
      id: existing?.id ?? `assembly-${crypto.randomUUID()}`,
      name: trimmedName,
      description: description.trim(),
      components,
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

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-[1fr_18rem]">
        <div className="flex flex-col gap-6">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="assembly-name">Assembly name</Label>
            <Input
              id="assembly-name"
              value={name}
              onChange={(event) => setName(event.target.value)}
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="assembly-description">Description</Label>
            <Textarea
              id="assembly-description"
              rows={2}
              value={description}
              onChange={(event) => setDescription(event.target.value)}
            />
          </div>

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
                    component={component}
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

        <SummarySidebar components={components} allAssemblies={assemblies} />
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
            <Button type="button" onClick={handleSave}>
              {mode === 'add' ? 'Create assembly' : 'Save changes'}
            </Button>
          </>
        }
      />

      <PartPickerDialog
        open={pickerOpen}
        onOpenChange={setPickerOpen}
        mode={editingComponent ? 'edit' : 'add'}
        initialPartId={editingComponent?.partId}
        initialQuantity={editingComponent?.quantity}
        onSubmit={handlePickerSubmit}
      />
    </div>
  );
}
