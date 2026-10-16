import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import type { RefObject } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate } from 'react-router';
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
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type {
  PartResponse,
  PartSummaryResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsPartsGetPartEndpoint } from '@/lib/api/generated/parts/parts';
import { getOpenFarmApiEndpointsPrintJobsListQueueJobsEndpointQueryKey } from '@/lib/api/generated/print-jobs/print-jobs';
import {
  getOpenFarmApiEndpointsProductionRunsListProductionRunsEndpointQueryKey,
  useOpenFarmApiEndpointsProductionRunsCreateProductionRunEndpoint,
} from '@/lib/api/generated/production-runs/production-runs';

const schema = z.object({
  quantity: z
    .number()
    .int('Enter a whole number of units.')
    .min(1, 'Enter at least 1 unit.')
    .max(2147483647),
});

export function QueuePartDialog({
  part,
  onClose,
  returnFocusRef,
}: {
  part: PartSummaryResponse;
  onClose: () => void;
  returnFocusRef?: RefObject<HTMLButtonElement | null>;
}) {
  const query = useOpenFarmApiEndpointsPartsGetPartEndpoint(part.id ?? '', {
    query: {
      staleTime: 0,
      refetchOnMount: 'always',
      refetchOnWindowFocus: false,
    },
  });
  const detail = query.data?.data;
  if (!query.isFetching && detail?.printRequirementsStatus === 'Complete') {
    return (
      <QueuePartForm
        part={detail}
        onClose={onClose}
        returnFocusRef={returnFocusRef}
      />
    );
  }
  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
    >
      <DialogContent
        onCloseAutoFocus={(event) => {
          if (returnFocusRef?.current) {
            event.preventDefault();
            returnFocusRef.current.focus();
          }
        }}
      >
        <DialogHeader>
          <DialogTitle>Send part to queue</DialogTitle>
          <DialogDescription>
            Queue {part.name} from the part library.
          </DialogDescription>
        </DialogHeader>
        {query.isFetching ? (
          <p role="status">Loading part…</p>
        ) : (
          <>
            <p role="alert">
              {query.isError || !detail
                ? 'Could not load part. Try again.'
                : 'Complete this part’s print requirements before queueing.'}
            </p>
            <DialogFooter>
              <DialogClose asChild>
                <Button variant="outline">Cancel</Button>
              </DialogClose>
              {query.isError && (
                <Button onClick={() => query.refetch()}>Retry</Button>
              )}
            </DialogFooter>
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}

function QueuePartForm({
  part,
  onClose,
  returnFocusRef,
}: {
  part: PartResponse;
  onClose: () => void;
  returnFocusRef?: RefObject<HTMLButtonElement | null>;
}) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const unitsPerPrint = part.unitsPerPrint ?? 1;
  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { quantity: unitsPerPrint },
  });
  const mutation =
    useOpenFarmApiEndpointsProductionRunsCreateProductionRunEndpoint();
  const quantity = form.watch('quantity');
  const plates =
    Number.isInteger(quantity) && quantity > 0
      ? Math.ceil(quantity / unitsPerPrint)
      : null;

  async function onSubmit(values: z.infer<typeof schema>) {
    if (!part.id || mutation.isPending) return;
    form.clearErrors('root');
    try {
      await mutation.mutateAsync({
        data: {
          lines: [
            {
              targetKind: 'Part',
              targetId: part.id,
              quantity: values.quantity,
              assignmentMode: 'Unassigned',
              revisionMode: 'Snapshot',
            },
          ],
        },
      });
      queryClient.invalidateQueries({
        queryKey:
          getOpenFarmApiEndpointsPrintJobsListQueueJobsEndpointQueryKey(),
      });
      queryClient.invalidateQueries({
        queryKey:
          getOpenFarmApiEndpointsProductionRunsListProductionRunsEndpointQueryKey(),
      });
      toast.success(`${part.name ?? 'Part'} sent to the queue.`, {
        action: { label: 'View queue', onClick: () => navigate('/jobs') },
      });
      onClose();
    } catch (error) {
      form.setError('root.server', {
        message: await extractErrorDetail(
          error,
          'Could not send part to queue.',
        ),
      });
    }
  }

  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open && !mutation.isPending) onClose();
      }}
    >
      <DialogContent
        onCloseAutoFocus={(event) => {
          if (returnFocusRef?.current) {
            event.preventDefault();
            returnFocusRef.current.focus();
          }
        }}
      >
        <DialogHeader>
          <DialogTitle>Send part to queue</DialogTitle>
          <DialogDescription>
            Queue {part.name} using its current file revision. A matching
            printer will be assigned automatically.
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
                name="quantity"
                render={({ field }) => (
                  <FormItem data-disabled={mutation.isPending}>
                    <FormLabel>Quantity (units)</FormLabel>
                    <FormControl>
                      <Input
                        {...field}
                        type="number"
                        min={1}
                        step={1}
                        disabled={mutation.isPending}
                        value={Number.isNaN(field.value) ? '' : field.value}
                        onChange={(event) =>
                          field.onChange(event.target.valueAsNumber)
                        }
                      />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <p className="text-sm text-muted-foreground">
                {unitsPerPrint} units per plate.
                {plates !== null &&
                  ` ${plates} print ${plates === 1 ? 'job' : 'jobs'} · ${plates * unitsPerPrint} units will be produced.`}
              </p>
              {form.formState.errors.root?.server?.message && (
                <p role="alert" className="text-sm text-destructive">
                  {form.formState.errors.root.server.message}
                </p>
              )}
              <DialogFooter>
                <Button
                  type="button"
                  variant="outline"
                  disabled={mutation.isPending}
                  onClick={onClose}
                >
                  Cancel
                </Button>
                <Button type="submit" disabled={mutation.isPending}>
                  {mutation.isPending && <Spinner data-icon="inline-start" />}
                  {mutation.isPending ? 'Queueing…' : 'Send to queue'}
                </Button>
              </DialogFooter>
            </FieldGroup>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
}
