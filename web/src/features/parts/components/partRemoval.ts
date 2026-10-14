import { openFarmApiEndpointsPartsCheckPartDeletionEndpoint } from '@/lib/api/generated/parts/parts';

export interface DeletionBlocker {
  code: string;
  entityType: string;
  entityId: string;
  name: string;
}
export interface DeletionEligibility {
  partId: string;
  name?: string | null;
  status: 'Eligible' | 'Blocked' | 'Absent';
  blockers: DeletionBlocker[];
  legacyJobsLosingRecovery: number;
}
export async function checkPartDeletion(
  partIds: string[],
  signal?: AbortSignal,
) {
  const response = await openFarmApiEndpointsPartsCheckPartDeletionEndpoint(
    { partIds },
    { signal },
  );
  if (response.status !== 200) throw new Error('Deletion check failed');
  const results = response.data.parts;
  // Never permit deletion based on an incomplete or malformed preview.
  if (
    !Array.isArray(results) ||
    results.length !== partIds.length ||
    partIds.some(
      (id) => results.filter((p) => p?.partId === id).length !== 1,
    ) ||
    results.some(
      (p) =>
        !p ||
        !['Eligible', 'Blocked', 'Absent'].includes(p.status ?? '') ||
        !Array.isArray(p.blockers) ||
        !Number.isInteger(p.legacyJobsLosingRecovery) ||
        (p.legacyJobsLosingRecovery ?? -1) < 0 ||
        p.blockers.some(
          (b) =>
            !b ||
            typeof b.code !== 'string' ||
            typeof b.entityId !== 'string' ||
            typeof b.name !== 'string' ||
            !['Assembly', 'ProductionRun', 'PrintJob'].includes(
              b.entityType ?? '',
            ),
        ) ||
        (p.status === 'Eligible' && p.blockers.length !== 0),
    )
  ) {
    throw new Error('Invalid deletion check response');
  }
  return results as DeletionEligibility[];
}

export async function removalError(error: unknown) {
  const status = error instanceof Response ? error.status : undefined;
  let problem: {
    detail?: string;
    code?: string;
    blockers?: DeletionBlocker[];
  } = {};
  if (error instanceof Response) {
    try {
      const parsed = await error.clone().json();
      if (parsed && typeof parsed === 'object') problem = parsed;
    } catch {
      /* Non-JSON response. */
    }
  }
  return {
    reason:
      typeof problem.detail === 'string'
        ? problem.detail
        : 'Could not complete the request. Check again before retrying.',
    status,
    code: problem.code,
    blockers: Array.isArray(problem.blockers)
      ? problem.blockers.filter(
          (b) =>
            b &&
            typeof b.code === 'string' &&
            typeof b.name === 'string' &&
            typeof b.entityId === 'string' &&
            ['Assembly', 'ProductionRun', 'PrintJob'].includes(b.entityType),
        )
      : [],
    retryable:
      (status === undefined && error instanceof TypeError) ||
      status === 429 ||
      (status !== undefined && status >= 500 && status <= 599),
  };
}
