import { zodResolver } from '@hookform/resolvers/zod';
import type { ReactNode } from 'react';
import { useEffect } from 'react';
import { type UseFormReturn, useForm, useWatch } from 'react-hook-form';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { FormFooter } from '@/components/form-footer/FormFooter';
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormMessage,
} from '@/components/ui/form';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import type { AssemblyResponse } from '@/lib/api/generated/endpoints.schemas';
import { BomLineEditor, type EditableLine } from './BomLineEditor';
import { BuildPanel } from './BuildPanel';

const schema = z.object({
  name: z.string().trim().min(1, 'Name required'),
  description: z.string().optional(),
  lines: z.array(z.custom<EditableLine>()).min(1, 'Add at least one line.'),
});

export type AssemblyFormValues = z.infer<typeof schema>;

function toFormValues(assembly?: AssemblyResponse): AssemblyFormValues {
  return {
    name: assembly?.name ?? '',
    description: assembly?.description ?? '',
    lines: (assembly?.lines ?? []).map((line) => ({
      key: line.id ?? crypto.randomUUID(),
      lineId: line.id,
      targetKind: (line.targetKind ?? 'Part') as EditableLine['targetKind'],
      targetId: line.targetId ?? '',
      targetName: line.targetName ?? '',
      quantity: line.quantity ?? 1,
    })),
  };
}

/** Owns the form so pages keep isDirty/markClean for their own navigation. Re-seeds only when the assembly's identity changes, never on refetch (which would clobber the draft). */
export function useAssemblyForm(assembly?: AssemblyResponse) {
  const form = useForm<AssemblyFormValues>({
    resolver: zodResolver(schema),
    defaultValues: toFormValues(),
  });

  // biome-ignore lint/correctness/useExhaustiveDependencies: see above — keyed on identity only.
  useEffect(() => {
    if (assembly) form.reset(toFormValues(assembly));
  }, [assembly?.id]);

  return {
    form,
    resetTo: (next: AssemblyResponse) => form.reset(toFormValues(next)),
  };
}

export function toLinePayload(line: EditableLine) {
  return {
    targetKind: line.targetKind,
    targetId: line.targetId,
    quantity: line.quantity,
  };
}

interface AssemblyEditorProps {
  form: UseFormReturn<AssemblyFormValues>;
  onSubmit: (values: AssemblyFormValues) => void;
  /** Unset on create; hides this assembly from the line picker. */
  assemblyId?: string;
  headerAction?: ReactNode;
  footerActions: ReactNode;
}

export function AssemblyEditor({
  form,
  onSubmit,
  assemblyId,
  headerAction,
  footerActions,
}: AssemblyEditorProps) {
  const { isDirty } = form.formState;
  const lines = useWatch({ control: form.control, name: 'lines' });

  return (
    <Form {...form}>
      <form
        id="assembly-form"
        onSubmit={form.handleSubmit(onSubmit)}
        className="flex flex-1 flex-col gap-6"
      >
        <header className="flex flex-wrap items-start justify-between gap-4">
          <div className="flex min-w-0 flex-1 flex-col gap-0.5">
            <BackLink to="/assemblies" className="mb-1">
              Back to assemblies
            </BackLink>
            <FormField
              control={form.control}
              name="name"
              render={({ field }) => (
                <FormItem className="gap-1">
                  <FormControl>
                    <Input
                      {...field}
                      aria-label="Name"
                      placeholder="Untitled assembly"
                      className="-ml-2 h-auto border-transparent bg-transparent px-2 py-1 font-heading text-2xl font-semibold shadow-none hover:border-input md:text-2xl dark:bg-transparent"
                    />
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
                  <FormControl>
                    <Textarea
                      {...field}
                      aria-label="Description"
                      placeholder="Add a description"
                      rows={1}
                      className="-ml-2 min-h-0 resize-none border-transparent bg-transparent px-2 py-1 text-muted-foreground shadow-none field-sizing-content hover:border-input dark:bg-transparent"
                    />
                  </FormControl>
                </FormItem>
              )}
            />
          </div>
          {headerAction}
        </header>

        <div className="grid flex-1 items-start gap-6 lg:grid-cols-[minmax(0,1fr)_18rem]">
          <FormField
            control={form.control}
            name="lines"
            render={({ field }) => (
              <div className="flex flex-col gap-2">
                <BomLineEditor
                  lines={field.value}
                  onChange={field.onChange}
                  excludeAssemblyId={assemblyId}
                />
                <FormMessage />
              </div>
            )}
          />
          <BuildPanel lines={lines} className="lg:sticky lg:top-4" />
        </div>

        <FormFooter
          end={
            <>
              {isDirty && (
                <span className="mr-2 text-sm text-muted-foreground">
                  Unsaved changes
                </span>
              )}
              {footerActions}
            </>
          }
        />
      </form>
    </Form>
  );
}
