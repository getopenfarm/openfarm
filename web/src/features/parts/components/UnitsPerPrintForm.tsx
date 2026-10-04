import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { toast } from 'sonner';
import { z } from 'zod';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { useOpenFarmApiEndpointsPartsSetPartUnitsPerPrintEndpoint } from '@/lib/api/generated/parts/parts';

const schema = z.object({ unitsPerPrint: z.number().int().min(1) });

export function UnitsPerPrintForm({
  partId,
  unitsPerPrint,
}: {
  partId: string;
  unitsPerPrint: number;
}) {
  const queryClient = useQueryClient();
  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { unitsPerPrint },
  });
  const mutation = useOpenFarmApiEndpointsPartsSetPartUnitsPerPrintEndpoint({
    mutation: {
      onSuccess: () => {
        queryClient.invalidateQueries();
        toast.success('Units per print saved.');
      },
      onError: () => toast.error('Could not save units per print.'),
    },
  });
  return (
    <form
      className="flex items-end gap-3"
      onSubmit={form.handleSubmit((data) =>
        mutation.mutate({ id: partId, data }),
      )}
    >
      <div className="space-y-1">
        <Label htmlFor="units-per-print">Units per print</Label>
        <Input
          id="units-per-print"
          type="number"
          min={1}
          {...form.register('unitsPerPrint', { valueAsNumber: true })}
        />
        {form.formState.errors.unitsPerPrint && (
          <p role="alert" className="text-sm text-destructive">
            Enter a whole number of at least 1.
          </p>
        )}
        <p className="text-xs text-muted-foreground">
          Used to calculate whole plates for future jobs.
        </p>
      </div>
      <Button type="submit" disabled={mutation.isPending}>
        Save
      </Button>
    </form>
  );
}
