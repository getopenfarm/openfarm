import { PlusIcon } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import type { MockJob } from '../data/mockJobsStore';

interface JobsHeaderProps {
  jobs: MockJob[];
}

export function JobsHeader({ jobs }: JobsHeaderProps) {
  const printingCount = jobs.filter((job) => job.status === 'printing').length;
  const queuedCount = jobs.filter((job) => job.status === 'queued').length;

  return (
    <div className="flex items-center justify-between">
      <div>
        <h2 className="font-heading text-2xl font-semibold">Print queue</h2>
        <p className="text-sm text-muted-foreground">
          {jobs.length} jobs · {printingCount} printing · {queuedCount} queued
        </p>
      </div>
      <Button onClick={() => toast("Creating jobs isn't implemented yet.")}>
        <PlusIcon />
        New job
      </Button>
    </div>
  );
}
