import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRef, useState } from 'react';
import { toast } from 'sonner';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import type {
  FeeProfileDto,
  FeesResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { httpClient } from '@/lib/api/httpClient';
import { feesKey } from './fees';

export function useFeeProfileRemoval(onRemoved?: () => void) {
  const client = useQueryClient();
  const [profile, setProfile] = useState<FeeProfileDto | null>(null);
  const returnFocusRef = useRef<HTMLElement | null>(null);
  const fallbackFocusRef = useRef<HTMLAnchorElement | null>(null);
  const remove = useMutation({
    mutationFn: (target: FeeProfileDto) =>
      httpClient(`${feesKey}/profiles/${target.id}`, { method: 'DELETE' }),
    onSuccess: (_, target) => {
      client.setQueryData<{ data: FeesResponse }>([feesKey], (previous) =>
        previous
          ? {
              ...previous,
              data: {
                ...previous.data,
                profiles: previous.data.profiles?.filter(
                  (p) => p.id !== target.id,
                ),
              },
            }
          : previous,
      );
      void client.invalidateQueries({ queryKey: [feesKey] });
      returnFocusRef.current = null;
      setProfile(null);
      toast.success('Fee profile removed.');
      onRemoved?.();
    },
  });
  return {
    pending: remove.isPending,
    returnFocusRef,
    fallbackFocusRef,
    open: (target: FeeProfileDto) => {
      remove.reset();
      setProfile(target);
    },
    dialog: (
      <ConfirmDialog
        open={profile !== null}
        onOpenChange={(open) => {
          if (!open) setProfile(null);
        }}
        title={`Remove ${profile?.name ?? 'fee profile'}?`}
        description="This profile will no longer appear in assembly pricing estimates."
        confirmLabel={remove.isPending ? 'Removing…' : 'Remove'}
        destructive
        isPending={remove.isPending}
        closeOnConfirm={false}
        returnFocusRef={returnFocusRef}
        fallbackFocusRef={fallbackFocusRef}
        error={
          remove.isError
            ? 'Could not remove fee profile. Try again.'
            : undefined
        }
        onConfirm={() => {
          if (profile) remove.mutate(profile);
        }}
      />
    ),
  };
}
