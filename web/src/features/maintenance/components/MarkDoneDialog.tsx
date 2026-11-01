import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { ChevronDownIcon } from 'lucide-react';
import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { toast } from 'sonner';
import { z } from 'zod';
import { PermissionButton as Button } from '@/components/PermissionButton';
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from '@/components/ui/collapsible';
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
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
import { useCurrency } from '@/features/settings/generalSettings';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type { MaintenanceEnrollmentResponse } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsMaintenanceCompleteMaintenanceEndpoint } from '@/lib/api/generated/maintenance/maintenance';
import {
  invalidateMaintenanceQueries,
  startOfDayIso,
  toDateInput,
  todayInput,
} from '../maintenance';

/** `minDay`: the latest cycle start among the targets, as a date input value ('' = no bound). */
export const markDoneSchema = (minDay = '') =>
  z.object({
    notes: z.string().max(2000, 'Max 2000 characters.'),
    performedOn: z
      .string()
      .refine((v) => v !== '' && v <= todayInput(), 'Cannot be in the future.')
      .refine((v) => v >= minDay, 'Before last done.'),
    performedByNote: z.string().max(200, 'Max 200 characters.'),
    cost: z
      .string()
      .trim()
      .refine((v) => v === '' || Number(v) >= 0, 'Must be ≥ 0.'),
  });
type FormValues = z.infer<ReturnType<typeof markDoneSchema>>;
const defaults = (): FormValues => ({
  notes: '',
  performedOn: todayInput(),
  performedByNote: '',
  cost: '',
});

interface MarkDoneDialogProps {
  /** Enrollments to complete together; empty closes the dialog. */
  targets: MaintenanceEnrollmentResponse[];
  onOpenChange: (open: boolean) => void;
  onDone?: () => void;
}

/** Logs one completion per enrollment; all or none. Today = now; an earlier day = its local start. */
export function MarkDoneDialog({
  targets,
  onOpenChange,
  onDone,
}: MarkDoneDialogProps) {
  const queryClient = useQueryClient();
  const currency = useCurrency();
  const open = targets.length > 0;
  // The API rejects a completion before a target's cycle start, so the same day sends that start, not midnight.
  const cycleStart = targets
    .map((t) => t.cycleStartedAtUtc ?? '')
    .filter(Boolean)
    .reduce(
      (latest, iso) => (Date.parse(iso) > Date.parse(latest) ? iso : latest),
      '',
    );
  const minDay = cycleStart ? toDateInput(cycleStart) : '';
  const form = useForm<FormValues>({
    resolver: zodResolver(markDoneSchema(minDay)),
    defaultValues: defaults(),
  });
  useEffect(() => {
    if (open) form.reset(defaults());
  }, [open, form]);

  const complete =
    useOpenFarmApiEndpointsMaintenanceCompleteMaintenanceEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success(
            targets.length === 1
              ? 'Maintenance marked done.'
              : `${targets.length} tasks marked done.`,
          );
          invalidateMaintenanceQueries(queryClient);
          onOpenChange(false);
          onDone?.();
        },
        onError: async (error) =>
          toast.error(await extractErrorDetail(error, 'Could not mark done.')),
      },
    });

  function onSubmit(v: FormValues) {
    complete.mutate({
      data: {
        enrollmentIds: targets.map((t) => t.id ?? ''),
        performedAtUtc:
          v.performedOn === todayInput()
            ? null
            : v.performedOn === minDay
              ? cycleStart
              : startOfDayIso(v.performedOn),
        notes: v.notes.trim() || null,
        performedByNote: v.performedByNote.trim() || null,
        cost: v.cost === '' ? null : Number(v.cost),
      },
    });
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Mark done</DialogTitle>
          <DialogDescription>
            {targets.map((t) => `${t.taskName} · ${t.printerName}`).join(', ')}
          </DialogDescription>
        </DialogHeader>
        <Form {...form}>
          <form
            onSubmit={form.handleSubmit(onSubmit)}
            className="flex flex-col gap-4"
          >
            <FieldGroup className="gap-4">
              <FormField
                control={form.control}
                name="notes"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Notes</FormLabel>
                    <FormControl>
                      <Textarea rows={2} {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <Collapsible>
                <CollapsibleTrigger asChild>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    className="group -ml-2 self-start"
                  >
                    More
                    <ChevronDownIcon
                      data-icon="inline-end"
                      className="transition-transform group-data-[state=open]:rotate-180"
                    />
                  </Button>
                </CollapsibleTrigger>
                <CollapsibleContent className="grid grid-cols-1 gap-4 pt-2 sm:grid-cols-2">
                  <FormField
                    control={form.control}
                    name="performedOn"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Performed on</FormLabel>
                        <FormControl>
                          <Input
                            type="date"
                            min={minDay || undefined}
                            max={todayInput()}
                            {...field}
                          />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <FormField
                    control={form.control}
                    name="cost"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Cost ({currency})</FormLabel>
                        <FormControl>
                          <Input type="number" min={0} step="any" {...field} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <FormField
                    control={form.control}
                    name="performedByNote"
                    render={({ field }) => (
                      <FormItem className="sm:col-span-2">
                        <FormLabel>Performed by</FormLabel>
                        <FormControl>
                          <Input placeholder="Blank = you" {...field} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                </CollapsibleContent>
              </Collapsible>
              <DialogFooter>
                <DialogClose asChild>
                  <Button type="button" variant="outline">
                    Cancel
                  </Button>
                </DialogClose>
                <Button
                  permission="maintenance:complete"
                  type="submit"
                  disabled={complete.isPending}
                >
                  {complete.isPending && <Spinner data-icon="inline-start" />}
                  {complete.isPending ? 'Marking done…' : 'Mark done'}
                </Button>
              </DialogFooter>
            </FieldGroup>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
}
