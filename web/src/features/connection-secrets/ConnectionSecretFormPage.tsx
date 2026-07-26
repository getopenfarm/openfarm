import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { useNavigate } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '@/components/ui/form';
import { Input } from '@/components/ui/input';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import {
  getOpenFarmApiEndpointsConnectionSecretsListConnectionSecretsEndpointQueryKey,
  useOpenFarmApiEndpointsConnectionSecretsCreateConnectionSecretEndpoint,
} from '@/lib/api/generated/connection-secrets/connection-secrets';

const schema = z.object({
  printerSerialNumber: z.string().min(1, 'Printer serial number is required'),
  plainAccessCode: z.string().min(1, 'Access code is required'),
});

type FormValues = z.infer<typeof schema>;

export function ConnectionSecretFormPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { printerSerialNumber: '', plainAccessCode: '' },
  });

  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(
    form.formState.isDirty,
  );

  const createMutation =
    useOpenFarmApiEndpointsConnectionSecretsCreateConnectionSecretEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Connection secret created.');
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsConnectionSecretsListConnectionSecretsEndpointQueryKey(),
          });
          markClean();
          navigate('/settings/connection-secrets');
        },
        onError: () => {
          toast.error('Failed to create connection secret.');
        },
      },
    });

  function onSubmit(values: FormValues) {
    createMutation.mutate({ data: values });
  }

  return (
    <div className="flex flex-col gap-6">
      <BackLink to="/settings/connection-secrets">
        Back to connection secrets
      </BackLink>

      <PageHeader
        title="Add connection credential"
        description="The access code is encrypted at rest and never shown again after creation."
      />

      <Form {...form}>
        <form
          onSubmit={form.handleSubmit(onSubmit)}
          className="flex flex-col gap-6"
        >
          <Card>
            <CardContent className="flex flex-col gap-4">
              <FormField
                control={form.control}
                name="printerSerialNumber"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Printer serial number</FormLabel>
                    <FormControl>
                      <Input {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={form.control}
                name="plainAccessCode"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Access code</FormLabel>
                    <FormControl>
                      <Input type="password" {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
            </CardContent>
          </Card>

          <FormFooter
            start={
              <Button
                type="button"
                variant="ghost"
                onClick={() => navigate('/settings/connection-secrets')}
              >
                Cancel
              </Button>
            }
            end={
              <Button type="submit" disabled={createMutation.isPending}>
                {createMutation.isPending ? 'Saving…' : 'Save'}
              </Button>
            }
          />
        </form>
      </Form>

      <ConfirmLeaveDialog />
    </div>
  );
}
