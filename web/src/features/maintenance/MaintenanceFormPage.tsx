import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ChevronDownIcon } from 'lucide-react';
import { type ComponentProps, useEffect, useRef, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { BackLink } from '@/components/back-link/BackLink';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { PermissionButton as Button } from '@/components/PermissionButton';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Checkbox } from '@/components/ui/checkbox';
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from '@/components/ui/collapsible';
import {
  Field,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
} from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { Spinner } from '@/components/ui/spinner';
import { Textarea } from '@/components/ui/textarea';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type {
  ListFleetResponse,
  MaintenanceTaskResponse,
  MaintenanceTasksResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  openFarmApiEndpointsMaintenanceCreateMaintenanceTaskEndpoint,
  openFarmApiEndpointsMaintenanceEditMaintenanceTaskEndpoint,
  openFarmApiEndpointsMaintenanceEnrollPrintersEndpoint,
  openFarmApiEndpointsMaintenanceUnenrollPrinterEndpoint,
  useOpenFarmApiEndpointsMaintenanceListMaintenanceTasksEndpoint,
} from '@/lib/api/generated/maintenance/maintenance';
import { useOpenFarmApiEndpointsPrintersListFleetEndpoint } from '@/lib/api/generated/printers/printers';
import { invalidateMaintenanceQueries, todayInput } from './maintenance';
import {
  baselinePayload,
  formValues,
  type MaintenanceTaskFormValues,
  maintenanceTaskSchema,
  taskPayload,
} from './maintenanceForm';

type FieldName = Exclude<keyof MaintenanceTaskFormValues, 'printerIds'>;

/**
 * One form defines the task and signs printers up. On edit, the definition PUT changes every enrolled printer;
 * printers added are enrolled with the baseline below, printers cleared are unenrolled.
 */
export function MaintenanceFormPage({ mode }: { mode: 'add' | 'edit' }) {
  const { id } = useParams();
  const navigate = useNavigate();
  const client = useQueryClient();
  const form = useForm<MaintenanceTaskFormValues>({
    resolver: zodResolver(maintenanceTaskSchema),
    defaultValues: formValues(),
  });
  const { errors, isDirty } = form.formState;
  const [saveError, setSaveError] = useState<string | null>(null);
  const seeded = useRef<string | null>(null);
  const formRef = useRef<HTMLFormElement>(null);
  // No single-task GET: the edit page reads its task from the list.
  const tasksQuery =
    useOpenFarmApiEndpointsMaintenanceListMaintenanceTasksEndpoint({
      query: { enabled: mode === 'edit' },
    });
  const task = (
    tasksQuery.data?.data as MaintenanceTasksResponse | undefined
  )?.items?.find((t) => t.id === id);
  const printers =
    (
      useOpenFarmApiEndpointsPrintersListFleetEndpoint().data?.data as
        | ListFleetResponse
        | undefined
    )?.printers ?? [];
  useEffect(() => {
    if (task?.id && task.id !== seeded.current) {
      seeded.current = task.id;
      form.reset(formValues(task));
    }
  }, [task, form]);
  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(isDirty);
  const printerIds = useWatch({ control: form.control, name: 'printerIds' });
  const enrolled = task?.printerIds ?? [];
  const added = printerIds.filter((p) => !enrolled.includes(p));

  const save = useMutation({
    mutationFn: async (v: MaintenanceTaskFormValues) => {
      if (mode === 'add')
        return (
          await openFarmApiEndpointsMaintenanceCreateMaintenanceTaskEndpoint(
            taskPayload(v),
          )
        ).data as MaintenanceTaskResponse;
      const taskId = id ?? '';
      await openFarmApiEndpointsMaintenanceEditMaintenanceTaskEndpoint(
        taskId,
        taskPayload(v),
      );
      if (added.length)
        await openFarmApiEndpointsMaintenanceEnrollPrintersEndpoint(taskId, {
          printerIds: added,
          ...baselinePayload(v),
        });
      for (const printerId of enrolled.filter((p) => !v.printerIds.includes(p)))
        await openFarmApiEndpointsMaintenanceUnenrollPrinterEndpoint(
          taskId,
          printerId,
        );
      return undefined;
    },
    onSuccess: () => {
      markClean();
      setSaveError(null);
      toast.success(mode === 'add' ? 'Task added.' : 'Task saved.');
      navigate('/maintenance');
    },
    onError: async (error) =>
      setSaveError(await extractErrorDetail(error, 'Could not save task.')),
    // A partial edit (definition saved, an enroll failed) must still show.
    onSettled: () => invalidateMaintenanceQueries(client),
  });
  const busy = save.isPending;
  const back = <BackLink to="/maintenance">Back to maintenance</BackLink>;

  if (mode === 'edit' && (!task || seeded.current !== task.id))
    return (
      <Page>
        <PageHeader above={back} title="Task" />
        {tasksQuery.isPending || task ? (
          <Skeleton className="h-64 w-full" />
        ) : tasksQuery.isError ? (
          <ErrorState
            title="Could not load task"
            onRetry={() => tasksQuery.refetch()}
          />
        ) : (
          <EmptyState
            title="Task not found"
            description="Deleted or invalid link."
            action={
              <Button
                variant="outline"
                onClick={() => navigate('/maintenance')}
              >
                Back to maintenance
              </Button>
            }
          />
        )}
      </Page>
    );

  const field = (
    name: FieldName,
    label: string,
    props: ComponentProps<typeof Input> = {},
    description?: string,
  ) => (
    <Field data-invalid={!!errors[name] || undefined}>
      <FieldLabel htmlFor={`maintenance-${name}`}>{label}</FieldLabel>
      <Input
        id={`maintenance-${name}`}
        {...props}
        {...form.register(name)}
        aria-invalid={!!errors[name] || undefined}
      />
      {description && <FieldDescription>{description}</FieldDescription>}
      {errors[name] && <FieldError>{errors[name]?.message}</FieldError>}
    </Field>
  );
  const number = { type: 'number', min: 0, step: 'any' } as const;
  const whole = { type: 'number', min: 0, step: 1 } as const;
  const showBaseline = mode === 'add' || added.length > 0;

  return (
    <Page className="flex-1">
      <PageHeader
        above={back}
        title={mode === 'add' ? 'Add task' : (task?.name ?? 'Task')}
      />
      <form
        noValidate
        ref={formRef}
        className="flex flex-1 flex-col gap-6"
        aria-busy={busy}
        onSubmit={form.handleSubmit(
          (v) => {
            if (!busy) save.mutate(v);
          },
          () =>
            requestAnimationFrame(() =>
              formRef.current
                ?.querySelector<HTMLElement>('[aria-invalid="true"]')
                ?.focus(),
            ),
        )}
      >
        {saveError && (
          <Alert variant="destructive" role="alert">
            <AlertTitle>Could not save task</AlertTitle>
            <AlertDescription>{saveError}</AlertDescription>
          </Alert>
        )}
        <FieldGroup className="flex-1">
          <fieldset disabled={busy} className="flex min-w-0 flex-col gap-6">
            <Card>
              <CardHeader>
                <CardTitle>Task</CardTitle>
              </CardHeader>
              <CardContent className="grid grid-cols-1 items-start gap-4 sm:grid-cols-2 lg:grid-cols-4">
                <div className="sm:col-span-2">
                  {field('name', 'Name', { placeholder: 'Lubricate rods' })}
                </div>
                <div className="sm:col-span-2" />
                {field('everyPrintHours', 'Every hours', {
                  ...number,
                  placeholder: '—',
                })}
                {field('everyDays', 'Every days', {
                  ...whole,
                  placeholder: '—',
                })}
                {field('everyPrints', 'Every prints', {
                  ...whole,
                  placeholder: '—',
                })}
                {field('warnAtPercent', 'Warn at %', {
                  ...whole,
                  min: 1,
                  max: 99,
                })}
                <p className="text-xs text-muted-foreground sm:col-span-2 lg:col-span-4">
                  Due at whichever comes first.
                </p>
                <Field className="sm:col-span-2 lg:col-span-4">
                  <FieldLabel htmlFor="maintenance-notes">Notes</FieldLabel>
                  <Textarea
                    id="maintenance-notes"
                    rows={3}
                    {...form.register('notes')}
                  />
                </Field>
              </CardContent>
            </Card>
            <Card>
              <CardHeader>
                <CardTitle>Printers</CardTitle>
              </CardHeader>
              <CardContent className="flex flex-col gap-4">
                {printers.length === 0 ? (
                  <p className="text-sm text-muted-foreground">No printers.</p>
                ) : (
                  <fieldset
                    aria-label="Printers"
                    className="grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3"
                  >
                    {printers.map((p) => (
                      <div
                        key={p.id}
                        className="flex items-center gap-2 text-sm"
                      >
                        <Checkbox
                          id={`maintenance-printer-${p.id}`}
                          checked={printerIds.includes(p.id ?? '')}
                          onCheckedChange={(checked) =>
                            form.setValue(
                              'printerIds',
                              checked
                                ? [...printerIds, p.id ?? '']
                                : printerIds.filter((x) => x !== p.id),
                              { shouldDirty: true },
                            )
                          }
                        />
                        <label htmlFor={`maintenance-printer-${p.id}`}>
                          {p.name}{' '}
                          <span className="font-mono text-xs text-muted-foreground">
                            {p.modelCode}
                          </span>
                        </label>
                      </div>
                    ))}
                  </fieldset>
                )}
                {showBaseline && (
                  <div className="grid grid-cols-1 items-start gap-4 sm:grid-cols-2 lg:grid-cols-4">
                    {field(
                      'lastDone',
                      'Last done',
                      { type: 'date', max: todayInput() },
                      mode === 'edit'
                        ? 'Added printers. Blank = today.'
                        : 'Blank = today.',
                    )}
                    <Collapsible className="sm:col-span-2 lg:col-span-4">
                      <CollapsibleTrigger asChild>
                        <Button
                          type="button"
                          variant="ghost"
                          size="sm"
                          className="group -ml-2"
                        >
                          Usage before tracking
                          <ChevronDownIcon
                            data-icon="inline-end"
                            className="transition-transform group-data-[state=open]:rotate-180"
                          />
                        </Button>
                      </CollapsibleTrigger>
                      <CollapsibleContent className="grid grid-cols-1 items-start gap-4 pt-2 sm:grid-cols-2 lg:grid-cols-4">
                        {field(
                          'carryHours',
                          'Hours',
                          { ...number, placeholder: '0' },
                          'Since last service.',
                        )}
                        {field(
                          'carryPrints',
                          'Prints',
                          { ...whole, placeholder: '0' },
                          'Since last service.',
                        )}
                      </CollapsibleContent>
                    </Collapsible>
                  </div>
                )}
              </CardContent>
            </Card>
          </fieldset>
        </FieldGroup>
        <FormFooter
          end={
            <>
              {isDirty && (
                <span className="text-sm text-muted-foreground">
                  Unsaved changes
                </span>
              )}
              <Button
                type="button"
                variant="outline"
                disabled={busy}
                onClick={() => navigate('/maintenance')}
              >
                Cancel
              </Button>
              <Button
                permission="maintenance:manage"
                type="submit"
                disabled={busy || (mode === 'edit' && !isDirty)}
              >
                {busy && <Spinner data-icon="inline-start" />}
                {busy ? 'Saving…' : mode === 'add' ? 'Add task' : 'Save'}
              </Button>
            </>
          }
        />
      </form>
      <ConfirmLeaveDialog />
    </Page>
  );
}
