import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import type { RefObject } from 'react';
import { useForm } from 'react-hook-form';
import { toast } from 'sonner';
import { z } from 'zod';
import { Button } from '@/components/ui/button';
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
import { useOpenFarmApiEndpointsSpoolsRecordSpoolUsageEndpoint } from '@/lib/api/generated/spools/spools';
import { invalidateFilamentTrackingQueries } from '@/lib/invalidateFilamentTrackingQueries';

const schema = z.object({
  gramsConsumed: z
    .string()
    .refine((value) => Number(value) > 0, 'Must be greater than 0'),
});

type FormValues = z.infer<typeof schema>;

interface RecordUsageDialogProps {
  spoolId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  returnFocusRef?: RefObject<HTMLButtonElement | null>;
}

export function RecordUsageDialog({
  spoolId,
  open,
  onOpenChange,
  returnFocusRef,
}: RecordUsageDialogProps) {
  const queryClient = useQueryClient();

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { gramsConsumed: '' },
  });

  const recordUsageMutation =
    useOpenFarmApiEndpointsSpoolsRecordSpoolUsageEndpoint({
      mutation: {
        onSuccess: (response) => {
          const body = response.data;
          if (body?.autoContinuation?.occurred) {
            toast.success(
              body.autoContinuation.materialMismatch
                ? 'Usage recorded. Switched to a different material. Check the new spool.'
                : 'Usage recorded. Switched to the next spool.',
            );
          } else {
            toast.success('Usage recorded.');
          }
          form.reset();
          onOpenChange(false);
          invalidateFilamentTrackingQueries(queryClient);
        },
        onError: () => {
          toast.error('Could not record usage.');
        },
      },
    });

  function onSubmit(values: FormValues) {
    recordUsageMutation.mutate({
      id: spoolId,
      data: { gramsConsumed: Number(values.gramsConsumed) },
    });
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        onCloseAutoFocus={(event) => {
          if (returnFocusRef?.current?.isConnected) {
            event.preventDefault();
            returnFocusRef.current.focus();
          }
        }}
      >
        <DialogHeader>
          <DialogTitle>Record usage</DialogTitle>
          <DialogDescription>
            Record filament used since the last update.
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
                name="gramsConsumed"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Grams consumed</FormLabel>
                    <FormControl>
                      <Input type="number" min={1} {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <DialogFooter>
                <DialogClose asChild>
                  <Button type="button" variant="outline">
                    Cancel
                  </Button>
                </DialogClose>
                <Button type="submit" disabled={recordUsageMutation.isPending}>
                  {recordUsageMutation.isPending && (
                    <Spinner data-icon="inline-start" />
                  )}
                  {recordUsageMutation.isPending
                    ? 'Recording…'
                    : 'Record usage'}
                </Button>
              </DialogFooter>
            </FieldGroup>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
}
