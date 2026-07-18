import { PlusIcon } from 'lucide-react';
import { toast } from 'sonner';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { JobsTable } from './components/JobsTable';
import { useJobs } from './data/mockJobsStore';

export function JobsPage() {
  const [jobs] = useJobs();
  const printingCount = jobs.filter((job) => job.status === 'printing').length;
  const queuedCount = jobs.filter((job) => job.status === 'queued').length;

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Print queue"
        description={`${jobs.length} jobs · ${printingCount} printing · ${queuedCount} queued`}
        action={
          <Button onClick={() => toast("Creating jobs isn't implemented yet.")}>
            <PlusIcon />
            New job
          </Button>
        }
      />
      <JobsTable jobs={jobs} />
    </div>
  );
}
