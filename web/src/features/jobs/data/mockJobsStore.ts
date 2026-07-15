import { createMockStore } from '@/lib/mock-store/createMockStore';
import { SEED_JOBS } from './mockJobs';

export interface MockJob {
  id: string;
  name: string; // e.g. "Gridfinity bins ×24"
  partName?: string;
  priority: 'High' | 'Normal' | 'Low';
  printerName: string | null; // null/unassigned for queued jobs
  filamentColorHex?: string;
  filamentMaterial?: string;
  status: 'printing' | 'queued' | 'paused' | 'completed' | 'failed';
  progressPercent?: number | null;
  etaText?: string | null;
}

export const jobsStore = createMockStore<MockJob>(SEED_JOBS);

export function useJobs() {
  return jobsStore.useStore();
}
