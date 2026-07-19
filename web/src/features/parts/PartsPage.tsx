import { PlusIcon } from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import type { ListPartsResponse } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsPartsListPartsEndpoint } from '@/lib/api/generated/parts/parts';
import { AddOrReplacePartDialog } from './components/AddOrReplacePartDialog';
import { PartFilterBar } from './components/PartFilterBar';
import { PartsTable } from './components/PartsTable';
import { usePartsHub } from './hooks/usePartsHub';

const PAGE_SIZE = 500;

export function PartsPage() {
  const navigate = useNavigate();
  usePartsHub();

  const [selectedTags, setSelectedTags] = useState<string[]>([]);
  const [addOpen, setAddOpen] = useState(false);

  const partsQuery = useOpenFarmApiEndpointsPartsListPartsEndpoint({
    tag: selectedTags.length > 0 ? selectedTags : undefined,
    page: 1,
    pageSize: PAGE_SIZE,
  });

  const listData = partsQuery.data?.data as ListPartsResponse | undefined;
  const parts = listData?.parts ?? [];

  function toggleTag(tag: string) {
    setSelectedTags((prev) =>
      prev.includes(tag) ? prev.filter((t) => t !== tag) : [...prev, tag],
    );
  }

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Part library"
        description={
          listData ? `${listData.totalCount ?? parts.length} parts` : undefined
        }
        action={
          <Button onClick={() => setAddOpen(true)}>
            <PlusIcon /> Add part
          </Button>
        }
      />

      <PartFilterBar selectedTags={selectedTags} onToggle={toggleTag} />

      {partsQuery.isLoading ? (
        <Skeleton className="h-64 w-full" />
      ) : parts.length === 0 ? (
        <EmptyState
          title="No parts yet"
          description="Add an STL, 3MF, or G-code file to start your catalog."
          action={
            <Button onClick={() => setAddOpen(true)}>
              <PlusIcon /> Add part
            </Button>
          }
        />
      ) : (
        <PartsTable
          parts={parts}
          onRowClick={(part) => navigate(`/parts/${part.id}`)}
        />
      )}

      <AddOrReplacePartDialog
        mode="add"
        open={addOpen}
        onOpenChange={setAddOpen}
      />
    </div>
  );
}
