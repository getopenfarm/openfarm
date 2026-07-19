import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
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
  DialogTrigger,
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
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useFilamentCatalog } from '@/features/filament/data/useFilamentCatalog';
import {
  getOpenFarmApiEndpointsSpoolsListSpoolsEndpointQueryKey,
  useOpenFarmApiEndpointsSpoolsRegisterSpoolEndpoint,
} from '@/lib/api/generated/spools/spools';

const schema = z
  .object({
    filamentVariantId: z.string().min(1, 'Choose a filament colour'),
    label: z.string(),
    spoolWeightGrams: z
      .string()
      .refine((value) => Number(value) > 0, 'Must be greater than 0'),
    remainingWeightGrams: z.string(),
  })
  .refine(
    (values) =>
      values.remainingWeightGrams === '' ||
      Number(values.remainingWeightGrams) <= Number(values.spoolWeightGrams),
    {
      message: 'Remaining weight cannot exceed spool weight.',
      path: ['remainingWeightGrams'],
    },
  );

type FormValues = z.infer<typeof schema>;

export function AddSpoolDialog() {
  const [open, setOpen] = useState(false);
  const queryClient = useQueryClient();
  const filaments = useFilamentCatalog();

  const variantOptions = filaments.flatMap((filament) =>
    (filament.variants ?? []).map((variant) => ({
      id: variant.id ?? '',
      label: `${filament.vendorName} · ${filament.material} · ${variant.colorName}`,
    })),
  );

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      filamentVariantId: '',
      label: '',
      spoolWeightGrams: '1000',
      remainingWeightGrams: '',
    },
  });

  const registerMutation = useOpenFarmApiEndpointsSpoolsRegisterSpoolEndpoint({
    mutation: {
      onSuccess: () => {
        toast.success('Spool added.');
        form.reset();
        setOpen(false);
        queryClient.invalidateQueries({
          queryKey: getOpenFarmApiEndpointsSpoolsListSpoolsEndpointQueryKey(),
        });
      },
      onError: () => {
        toast.error('Could not add spool.');
      },
    },
  });

  function onSubmit(values: FormValues) {
    registerMutation.mutate({
      data: {
        filamentVariantId: values.filamentVariantId,
        label: values.label.trim() || null,
        spoolWeightGrams: Number(values.spoolWeightGrams),
        remainingWeightGrams:
          values.remainingWeightGrams === ''
            ? null
            : Number(values.remainingWeightGrams),
      },
    });
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button>Add spool</Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Add spool</DialogTitle>
          <DialogDescription>
            Register a physical spool of filament from the catalog.
          </DialogDescription>
        </DialogHeader>
        <Form {...form}>
          <form
            onSubmit={form.handleSubmit(onSubmit)}
            className="flex flex-col gap-4"
          >
            <FormField
              control={form.control}
              name="filamentVariantId"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Filament</FormLabel>
                  <Select value={field.value} onValueChange={field.onChange}>
                    <FormControl>
                      <SelectTrigger className="w-full">
                        <SelectValue placeholder="Choose a filament colour" />
                      </SelectTrigger>
                    </FormControl>
                    <SelectContent>
                      {variantOptions.map((option) => (
                        <SelectItem key={option.id} value={option.id}>
                          {option.label}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="label"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Label (optional)</FormLabel>
                  <FormControl>
                    <Input placeholder="e.g. Shelf B, spool 3" {...field} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="spoolWeightGrams"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Spool weight (g)</FormLabel>
                  <FormControl>
                    <Input type="number" min={1} {...field} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="remainingWeightGrams"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Remaining weight (g, optional)</FormLabel>
                  <FormControl>
                    <Input
                      type="number"
                      min={1}
                      placeholder="Defaults to full spool weight"
                      {...field}
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <DialogFooter>
              <Button type="submit" disabled={registerMutation.isPending}>
                {registerMutation.isPending ? 'Adding…' : 'Add spool'}
              </Button>
            </DialogFooter>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
}
