import { JobsHeader } from './components/JobsHeader';
import { JobsTable } from './components/JobsTable';
import { useJobs } from './data/mockJobsStore';

export function JobsPage() {
  const [jobs] = useJobs();

  return (
    <div className="flex flex-col gap-6">
      <JobsHeader jobs={jobs} />
      <JobsTable jobs={jobs} />
    </div>
  );
}
