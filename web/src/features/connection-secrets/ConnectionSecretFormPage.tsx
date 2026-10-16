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
import { Spinner } from '@/components/ui/spinner';
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
          toast.success('Credential added.');
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsConnectionSecretsListConnectionSecretsEndpointQueryKey(),
          });
          markClean();
          navigate('/settings/connection-secrets');
        },
        onError: () => {
          toast.error('Could not add credential.');
        },
      },
    });

  function onSubmit(values: FormValues) {
    createMutation.mutate({ data: values });
  }

  return (
    <Page className="flex-1">
      <PageHeader
        above={
          <BackLink to="/settings/connection-secrets" className="mb-1">
            Back to connection credentials
          </BackLink>
        }
        title="Add credential"
        description="Access code is encrypted and not shown again."
      />

      <Form {...form}>
        <form
          onSubmit={form.handleSubmit(onSubmit)}
          className="flex flex-1 flex-col gap-6"
        >
          <FieldGroup className="flex-1 gap-6">
            <Card>
              <CardHeader>
                <CardTitle>Credential</CardTitle>
              </CardHeader>
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
              end={
                <>
                  <Button
                    type="button"
                    variant="outline"
                    onClick={() => navigate('/settings/connection-secrets')}
                  >
                    Cancel
                  </Button>
                  <Button type="submit" disabled={createMutation.isPending}>
                    {createMutation.isPending && (
                      <Spinner data-icon="inline-start" />
                    )}
                    {createMutation.isPending ? 'Adding…' : 'Add credential'}
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
