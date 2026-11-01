import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { toast } from 'sonner';
import { z } from 'zod';
import { PermissionButton as Button } from '@/components/PermissionButton';
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
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type { MaintenanceEnrollmentResponse } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsMaintenanceRebaselineMaintenanceEndpoint } from '@/lib/api/generated/maintenance/maintenance';
import {
  invalidateMaintenanceQueries,
  startOfDayIso,
  toDateInput,
  todayInput,
} from '../maintenance';
import { carryFields } from '../maintenanceForm';

const schema = z.object({
  lastDone: z
    .string()
    .refine((v) => v !== '' && v <= todayInput(), 'Cannot be in the future.'),
  ...carryFields,
});
type FormValues = z.infer<typeof schema>;

/** Restarts one printer's cycle at a chosen day, with usage carried from before tracking. */
export function SetLastDoneDialog({
  target,
  onOpenChange,
}: {
  target: MaintenanceEnrollmentResponse | null;
  onOpenChange: (open: boolean) => void;
}) {
  const queryClient = useQueryClient();
  const form = useForm<FormValues>({ resolver: zodResolver(schema) });
  useEffect(() => {
    if (target)
      form.reset({
        lastDone: toDateInput(
          target.cycleStartedAtUtc ?? new Date().toISOString(),
        ),
        carryHours: String(target.carryHours ?? 0),
        carryPrints: String(target.carryPrints ?? 0),
      });
  }, [target, form]);

  const save = useOpenFarmApiEndpointsMaintenanceRebaselineMaintenanceEndpoint({
    mutation: {
      onSuccess: () => {
        toast.success('Last done saved.');
        invalidateMaintenanceQueries(queryClient);
        onOpenChange(false);
      },
      onError: async (error) =>
        toast.error(
          await extractErrorDetail(error, 'Could not save last done.'),
        ),
    },
  });

  return (
    <Dialog open={!!target} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Set last done</DialogTitle>
          <DialogDescription>
            {target?.taskName} · {target?.printerName}
          </DialogDescription>
        </DialogHeader>
        <Form {...form}>
          <form
            onSubmit={form.handleSubmit((v) =>
              save.mutate({
                id: target?.id ?? '',
                data: {
                  lastDoneAtUtc: startOfDayIso(v.lastDone),
                  carryHours: Number(v.carryHours || 0),
                  carryPrints: Number(v.carryPrints || 0),
                },
              }),
            )}
            className="flex flex-col gap-4"
          >
            <FieldGroup className="gap-4">
              <FormField
                control={form.control}
                name="lastDone"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Last done</FormLabel>
                    <FormControl>
                      <Input type="date" max={todayInput()} {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <div className="grid grid-cols-2 gap-4">
                <FormField
                  control={form.control}
                  name="carryHours"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>Hours before</FormLabel>
                      <FormControl>
                        <Input type="number" min={0} step="any" {...field} />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
                <FormField
                  control={form.control}
                  name="carryPrints"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>Prints before</FormLabel>
                      <FormControl>
                        <Input type="number" min={0} step={1} {...field} />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
              </div>
              <p className="text-xs text-muted-foreground">
                Usage since last service, before tracking.
              </p>
              <DialogFooter>
                <DialogClose asChild>
                  <Button type="button" variant="outline">
                    Cancel
                  </Button>
                </DialogClose>
                <Button
                  permission="maintenance:manage"
                  type="submit"
                  disabled={save.isPending}
                >
                  {save.isPending && <Spinner data-icon="inline-start" />}
                  {save.isPending ? 'Saving…' : 'Save'}
                </Button>
              </DialogFooter>
            </FieldGroup>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
}
