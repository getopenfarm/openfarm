import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import {
  getOpenFarmApiEndpointsProductionRunsGetProductionRunEndpointQueryKey,
  useOpenFarmApiEndpointsProductionRunsSetRunLineRevisionModeEndpoint,
} from '@/lib/api/generated/production-runs/production-runs';

interface RevisionModeControlProps {
  runId: string;
  lineId: string;
  revisionMode: string;
}

/**
 * FR-009b — editable after queueing; a change here affects only plates dispatched after this
 * call, never a currently-printing one (the running plate keeps whatever revision it already
 * started with).
 */
export function RevisionModeControl({
  runId,
  lineId,
  revisionMode,
}: RevisionModeControlProps) {
  const queryClient = useQueryClient();

  const mutation =
    useOpenFarmApiEndpointsProductionRunsSetRunLineRevisionModeEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Revision mode updated.');
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsProductionRunsGetProductionRunEndpointQueryKey(
                runId,
              ),
          });
        },
        onError: async (error) => {
          toast.error(
            await extractErrorDetail(error, 'Could not update revision mode.'),
          );
        },
      },
    });

  return (
    <Select
      value={revisionMode}
      disabled={mutation.isPending}
      onValueChange={(value) =>
        mutation.mutate({
          runId,
          lineId,
          data: { revisionMode: value },
        })
      }
    >
      <SelectTrigger className="h-6 w-36 text-xs" aria-label="Revision mode">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="Snapshot">Snapshot</SelectItem>
        <SelectItem value="FollowLatest">Follow-latest</SelectItem>
      </SelectContent>
    </Select>
  );
}
