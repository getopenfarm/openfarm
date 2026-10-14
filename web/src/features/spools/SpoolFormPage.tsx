import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { useNavigate } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
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
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Spinner } from '@/components/ui/spinner';
import { useFilamentCatalog } from '@/features/filament/data/useFilamentCatalog';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
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

export function SpoolFormPage() {
  const navigate = useNavigate();
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

  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(
    form.formState.isDirty,
  );

  const registerMutation = useOpenFarmApiEndpointsSpoolsRegisterSpoolEndpoint({
    mutation: {
      onSuccess: () => {
        toast.success('Spool added.');
        queryClient.invalidateQueries({
          queryKey: getOpenFarmApiEndpointsSpoolsListSpoolsEndpointQueryKey(),
        });
        markClean();
        navigate('/spools');
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
    <Page className="flex-1">
      <PageHeader
        above={
          <BackLink to="/spools" className="mb-1">
            Back to spools
          </BackLink>
        }
        title="Add spool"
        description="Register a physical spool of filament from the catalog."
      />

      <Form {...form}>
        <form
          onSubmit={form.handleSubmit(onSubmit)}
          className="flex flex-1 flex-col gap-6"
        >
          <FieldGroup className="flex-1 gap-6">
            <Card>
              <CardHeader>
                <CardTitle>Spool details</CardTitle>
              </CardHeader>
              <CardContent className="flex flex-col gap-4">
                <FormField
                  control={form.control}
                  name="filamentVariantId"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>Filament</FormLabel>
                      <Select
                        value={field.value}
                        onValueChange={field.onChange}
                      >
                        <FormControl>
                          <SelectTrigger className="w-full">
                            <SelectValue placeholder="Choose a filament colour" />
                          </SelectTrigger>
                        </FormControl>
                        <SelectContent>
                          <SelectGroup>
                            {variantOptions.map((option) => (
                              <SelectItem key={option.id} value={option.id}>
                                {option.label}
                              </SelectItem>
                            ))}
                          </SelectGroup>
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
              </CardContent>
            </Card>

            <FormFooter
              end={
                <>
                  <Button
                    type="button"
                    variant="outline"
                    onClick={() => navigate('/spools')}
                  >
                    Cancel
                  </Button>
                  <Button type="submit" disabled={registerMutation.isPending}>
                    {registerMutation.isPending && (
                      <Spinner data-icon="inline-start" />
                    )}
                    {registerMutation.isPending ? 'Saving…' : 'Save'}
                  </Button>
                </>
              }
            />
          </FieldGroup>
        </form>
      </Form>

      <ConfirmLeaveDialog />
    </Page>
  );
}
