import { useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router';
import { toast } from 'sonner';
import { PermissionButton as Button } from '@/components/PermissionButton';
import { Page } from '@/components/page/Page';
import { Spinner } from '@/components/ui/spinner';
import { invalidateLibraryContents } from '@/features/library/api';
import { LibraryItemBreadcrumb } from '@/features/library/LibraryBreadcrumb';
import { useLibraryLocation } from '@/features/library/useLibraryLocation';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import {
  getOpenFarmApiEndpointsAssembliesListAssembliesEndpointQueryKey,
  useOpenFarmApiEndpointsAssembliesCreateAssemblyEndpoint,
} from '@/lib/api/generated/assemblies/assemblies';
import {
  AssemblyEditor,
  type AssemblyFormValues,
  toLinePayload,
  toPricingPayload,
  useAssemblyForm,
} from './components/AssemblyEditor';

export function AssemblyFormPage() {
  const { folderId, location, itemLocation } = useLibraryLocation('assemblies');
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { form } = useAssemblyForm();
  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(
    form.formState.isDirty,
  );

  const createMutation =
    useOpenFarmApiEndpointsAssembliesCreateAssemblyEndpoint({
      mutation: {
        onSuccess: (created) => {
          void invalidateLibraryContents(queryClient);
          toast.success('Assembly added.');
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsAssembliesListAssembliesEndpointQueryKey(),
          });
          markClean();
          navigate(itemLocation(created.data?.id ?? ''));
        },
        onError: async (error) => {
          toast.error(
            await extractErrorDetail(error, 'Could not save assembly.'),
          );
        },
      },
    });

  function onSubmit(values: AssemblyFormValues) {
    createMutation.mutate({
      data: {
        ...(folderId ? { folderId } : {}),
        additionalCosts: values.additionalCosts,
        pricing: toPricingPayload(values.pricing),
        name: values.name.trim(),
        description: values.description?.trim() || null,
        lines: values.lines.map(toLinePayload),
      },
    });
  }

  const pending = createMutation.isPending;

  return (
    <Page className="flex-1">
      <LibraryItemBreadcrumb
        library="assemblies"
        folderId={folderId}
        name="Add assembly"
      />
      <AssemblyEditor
        form={form}
        onSubmit={onSubmit}
        footerActions={
          <>
            <Button
              type="button"
              variant="outline"
              onClick={() => navigate(location)}
            >
              Cancel
            </Button>
            <Button
              permission="assemblies:create"
              type="submit"
              disabled={pending}
            >
              {pending && <Spinner data-icon="inline-start" />}
              {pending ? 'Adding…' : 'Add assembly'}
            </Button>
          </>
        }
      />
      <ConfirmLeaveDialog />
    </Page>
  );
}
