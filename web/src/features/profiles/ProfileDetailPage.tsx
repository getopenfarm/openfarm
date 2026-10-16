import { useQueryClient } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { useForm } from 'react-hook-form';
import { Link, useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { BackLink } from '@/components/back-link/BackLink';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
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
import { Skeleton } from '@/components/ui/skeleton';
import { Spinner } from '@/components/ui/spinner';
import { Textarea } from '@/components/ui/textarea';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type {
  FilamentProfileDetailResponse,
  MachineProfileDetailResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsProfilesGetFilamentProfileEndpointQueryKey,
  getOpenFarmApiEndpointsProfilesGetMachineProfileEndpointQueryKey,
  getOpenFarmApiEndpointsProfilesListFilamentProfilesEndpointQueryKey,
  getOpenFarmApiEndpointsProfilesListMachineProfilesEndpointQueryKey,
  useOpenFarmApiEndpointsProfilesGetFilamentProfileEndpoint,
  useOpenFarmApiEndpointsProfilesGetMachineProfileEndpoint,
  useOpenFarmApiEndpointsProfilesUpdateFilamentProfileRawDataEndpoint,
  useOpenFarmApiEndpointsProfilesUpdateMachineProfileRawDataEndpoint,
} from '@/lib/api/generated/profiles/profiles';

type ProfileDetail = MachineProfileDetailResponse &
  FilamentProfileDetailResponse;

function formatRawDataJson(rawDataJson: string): string {
  try {
    return JSON.stringify(JSON.parse(rawDataJson), null, 2);
  } catch {
    return rawDataJson;
  }
}

export function ProfileDetailPage() {
  const { type, id = '' } = useParams<{ type: string; id: string }>();
  const isMachine = type === 'machine';
  const isFilament = type === 'filament';

  const machineQuery = useOpenFarmApiEndpointsProfilesGetMachineProfileEndpoint(
    id,
    { query: { enabled: isMachine } },
  );
  const filamentQuery =
    useOpenFarmApiEndpointsProfilesGetFilamentProfileEndpoint(id, {
      query: { enabled: isFilament },
    });
  const query = isMachine ? machineQuery : filamentQuery;
  const detail =
    query.data?.status === 200 ? (query.data.data as ProfileDetail) : undefined;

  const backLink = (
    <BackLink to="/profiles" className="mb-1">
      Back to profiles
    </BackLink>
  );

  if ((isMachine || isFilament) && query.isLoading) {
    return (
      <Page>
        <PageHeader above={backLink} title="Profile" />
        <Skeleton className="h-96 w-full" />
      </Page>
    );
  }

  if (
    query.isError &&
    !(
      (query.error as unknown) instanceof Response &&
      (query.error as unknown as Response).status === 404
    )
  ) {
    return (
      <Page>
        <PageHeader above={backLink} title="Profile" />
        <ErrorState
          title="Could not load profile"
          onRetry={() => query.refetch()}
        />
      </Page>
    );
  }

  if (!detail || !(isMachine || isFilament)) {
    return (
      <Page>
        <PageHeader above={backLink} title="Profile" />
        <EmptyState
          title="Profile not found"
          description="Removed or invalid link."
          action={
            <Button asChild variant="outline">
              <Link to="/profiles">Back to profiles</Link>
            </Button>
          }
        />
      </Page>
    );
  }

  return (
    <RawDataForm
      key={id}
      kind={isMachine ? 'machine' : 'filament'}
      detail={detail}
      backLink={backLink}
    />
  );
}

function RawDataForm({
  kind,
  detail,
  backLink,
}: {
  kind: 'machine' | 'filament';
  detail: ProfileDetail;
  backLink: ReactNode;
}) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const id = detail.id ?? '';

  const form = useForm<{ rawDataJson: string }>({
    defaultValues: { rawDataJson: formatRawDataJson(detail.rawDataJson ?? '') },
  });
  const { isDirty } = form.formState;
  const { ConfirmLeaveDialog } = useUnsavedChangesPrompt(isDirty);

  const updateMachine =
    useOpenFarmApiEndpointsProfilesUpdateMachineProfileRawDataEndpoint();
  const updateFilament =
    useOpenFarmApiEndpointsProfilesUpdateFilamentProfileRawDataEndpoint();
  const pending = updateMachine.isPending || updateFilament.isPending;

  async function onSubmit({ rawDataJson }: { rawDataJson: string }) {
    form.clearErrors('root');
    try {
      if (kind === 'machine') {
        await updateMachine.mutateAsync({ id, data: { rawDataJson } });
      } else {
        await updateFilament.mutateAsync({ id, data: { rawDataJson } });
      }
    } catch (error) {
      form.setError('root', {
        message: await extractErrorDetail(
          error,
          'Could not save profile. Check that raw data is valid JSON.',
        ),
      });
      return;
    }
    form.reset({ rawDataJson });
    queryClient.invalidateQueries({
      queryKey:
        kind === 'machine'
          ? getOpenFarmApiEndpointsProfilesGetMachineProfileEndpointQueryKey(id)
          : getOpenFarmApiEndpointsProfilesGetFilamentProfileEndpointQueryKey(
              id,
            ),
    });
    queryClient.invalidateQueries({
      queryKey:
        kind === 'machine'
          ? getOpenFarmApiEndpointsProfilesListMachineProfilesEndpointQueryKey()
          : getOpenFarmApiEndpointsProfilesListFilamentProfilesEndpointQueryKey(),
    });
    toast.success('Profile saved.');
  }

  const description = [
    kind === 'machine' ? 'Machine profile' : 'Filament profile',
    detail.baseProfileName
      ? `Based on ${detail.baseProfileName}`
      : 'Base profile',
    detail.brand,
    detail.sourceFormat,
  ]
    .filter(Boolean)
    .join(' · ');

  return (
    <Page className="flex-1">
      <PageHeader
        above={backLink}
        title={detail.name ?? 'Profile'}
        description={description}
      />

      <Form {...form}>
        <form
          onSubmit={form.handleSubmit(onSubmit)}
          className="flex flex-1 flex-col gap-6"
        >
          <FieldGroup className="flex-1 gap-6">
            <Card>
              <CardHeader>
                <CardTitle>Raw data</CardTitle>
              </CardHeader>
              <CardContent className="flex flex-col gap-3">
                <FormField
                  control={form.control}
                  name="rawDataJson"
                  rules={{ required: 'Raw data is required' }}
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel className="sr-only">Raw data JSON</FormLabel>
                      <FormControl>
                        <Textarea
                          {...field}
                          className="h-[60vh] font-mono text-xs"
                          spellCheck={false}
                        />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
                {form.formState.errors.root && (
                  <p className="text-sm text-destructive" role="alert">
                    {form.formState.errors.root.message}
                  </p>
                )}
              </CardContent>
            </Card>

            <FormFooter
              end={
                <>
                  <Button
                    type="button"
                    variant="outline"
                    onClick={() => navigate('/profiles')}
                  >
                    Cancel
                  </Button>
                  <Button type="submit" disabled={!isDirty || pending}>
                    {pending && <Spinner data-icon="inline-start" />}
                    {pending ? 'Saving…' : 'Save'}
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
