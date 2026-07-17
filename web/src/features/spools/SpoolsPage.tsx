import { PlusIcon } from 'lucide-react';
import { toast } from 'sonner';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { SpoolsTable } from './components/SpoolsTable';
import { useSpools } from './data/mockSpoolsStore';

const LOW_REMAINING_FRACTION = 0.25;

export function SpoolsPage() {
  const [spools] = useSpools();

  const inUseCount = spools.filter((spool) => spool.state === 'using').length;
  const lowCount = spools.filter(
    (spool) =>
      spool.state === 'using' &&
      spool.remainingGrams / spool.totalGrams < LOW_REMAINING_FRACTION,
  ).length;

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Spools"
        description={
          <>
            Physical inventory · {spools.length} spools ·{' '}
            <span className="text-kind-green font-medium">
              {inUseCount} in use
            </span>{' '}
            · <span className="text-kind-red font-medium">{lowCount} low</span>
          </>
        }
        action={
          <Button
            onClick={() =>
              toast.info("Adding spools isn't wired up in this preview yet.")
            }
          >
            <PlusIcon />
            Add spool
          </Button>
        }
      />
      <SpoolsTable spools={spools} />
    </div>
  );
}
