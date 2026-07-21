import { ListIcon, PlusIcon } from 'lucide-react';
import { toast } from 'sonner';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { JobsTable } from './components/JobsTable';
import { useJobs } from './data/mockJobsStore';

export function JobsPage() {
  const [jobs] = useJobs();
  const printingCount = jobs.filter((job) => job.status === 'printing').length;
  const queuedCount = jobs.filter((job) => job.status === 'queued').length;

  function handleNewJob() {
    toast("Creating jobs isn't implemented yet.");
  }

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Print queue"
        description={`${jobs.length} jobs · ${printingCount} printing · ${queuedCount} queued`}
        action={
          <Button onClick={handleNewJob}>
            <PlusIcon />
            New job
          </Button>
        }
      />
      {jobs.length === 0 ? (
        <EmptyState
          icon={<ListIcon className="size-8 text-muted-foreground" />}
          title="No jobs in the queue"
          description="Queue a print from a part or assembly to see it here."
          action={
            <Button onClick={handleNewJob}>
              <PlusIcon />
              New job
            </Button>
          }
        />
      ) : (
        <JobsTable jobs={jobs} />
      )}
    </div>
  );
}
