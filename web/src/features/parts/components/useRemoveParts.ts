import { useQueryClient } from '@tanstack/react-query';
import { useRef, useState } from 'react';
import {
  getOpenFarmApiEndpointsPartsGetPartEndpointQueryKey,
  useOpenFarmApiEndpointsPartsDeletePartEndpoint,
} from '@/lib/api/generated/parts/parts';
import { type DeletionBlocker, removalError } from './partRemoval';

export interface RemovablePart {
  id: string;
  name: string;
}
export interface PartRemovalOutcome {
  part: RemovablePart;
  status:
    | 'Removed'
    | 'Absent'
    | 'Blocked'
    | 'Failed'
    | 'Removing'
    | 'NotAttempted'
    | 'Recheck';
  reason?: string;
  retryable?: boolean;
  blockers?: DeletionBlocker[];
  httpStatus?: number;
}

export function useRemoveParts() {
  const client = useQueryClient();
  const mutation = useOpenFarmApiEndpointsPartsDeletePartEndpoint({
    mutation: { retry: false },
  });
  const running = useRef(false);
  const [isPending, setPending] = useState(false);

  async function refresh(ids: string[]) {
    for (const id of ids) {
      const queryKey = getOpenFarmApiEndpointsPartsGetPartEndpointQueryKey(id);
      await client.cancelQueries({ queryKey });
      client.removeQueries({ queryKey });
    }
    await client.invalidateQueries({
      predicate: (query) =>
        query.queryKey[0] === '/api/v1/library' ||
        query.queryKey[0] === '/api/v1/parts' ||
        query.queryKey[0] === '/api/v1/tags' ||
        (typeof query.queryKey[0] === 'string' &&
          (query.queryKey[0].startsWith('/api/v1/production-runs') ||
            query.queryKey[0].startsWith('/api/v1/print-jobs'))),
    });
  }

  async function remove(
    parts: RemovablePart[],
    onProgress: (outcome: PartRemovalOutcome) => void,
    onPendingChange?: (pending: boolean) => void,
  ) {
    if (running.current) return;
    running.current = true;
    setPending(true);
    onPendingChange?.(true);
    const outcomes: PartRemovalOutcome[] = [];
    try {
      for (const part of parts) {
        onProgress({ part, status: 'Removing' });
        let outcome: PartRemovalOutcome;
        try {
          await mutation.mutateAsync({ id: part.id });
          outcome = { part, status: 'Removed' };
        } catch (error) {
          const failure = await removalError(error);
          outcome = {
            part,
            ...failure,
            httpStatus: failure.status,
            status:
              failure.status === 404
                ? 'Absent'
                : failure.code === 'PartDeletionBlocked'
                  ? 'Blocked'
                  : failure.status === 409
                    ? 'Recheck'
                    : 'Failed',
          };
        }
        outcomes.push(outcome);
        onProgress(outcome);
        if (outcome.httpStatus === 401 || outcome.httpStatus === 403) {
          for (const skipped of parts.slice(outcomes.length)) {
            const notAttempted: PartRemovalOutcome = {
              part: skipped,
              status: 'NotAttempted',
              reason:
                'Removal stopped because access was denied. Check permissions before checking again.',
            };
            outcomes.push(notAttempted);
            onProgress(notAttempted);
          }
          break;
        }
      }
      return outcomes;
    } finally {
      running.current = false;
      setPending(false);
      onPendingChange?.(false);
    }
  }
  return { remove, refresh, isPending };
}
