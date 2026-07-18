import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { toast } from 'sonner';
import { z } from 'zod';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '@/components/ui/form';
import { Input } from '@/components/ui/input';
import {
  getOpenFarmApiEndpointsSpoolsListSpoolsEndpointQueryKey,
  useOpenFarmApiEndpointsSpoolsRecordSpoolUsageEndpoint,
} from '@/lib/api/generated/spools/spools';

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
}

export function RecordUsageDialog({
  spoolId,
  open,
  onOpenChange,
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
                ? 'Usage recorded. Auto-continued onto a different material — check the new spool.'
                : 'Usage recorded. Auto-continued onto the next spool.',
            );
          } else {
            toast.success('Usage recorded.');
          }
          form.reset();
          onOpenChange(false);
          queryClient.invalidateQueries({
            queryKey: getOpenFarmApiEndpointsSpoolsListSpoolsEndpointQueryKey(),
          });
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
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Record usage</DialogTitle>
          <DialogDescription>
            Log filament consumed from this spool since it was last updated.
          </DialogDescription>
        </DialogHeader>
        <Form {...form}>
          <form
            onSubmit={form.handleSubmit(onSubmit)}
            className="flex flex-col gap-4"
          >
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
              <Button type="submit" disabled={recordUsageMutation.isPending}>
                {recordUsageMutation.isPending ? 'Recording…' : 'Record usage'}
              </Button>
            </DialogFooter>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
}
