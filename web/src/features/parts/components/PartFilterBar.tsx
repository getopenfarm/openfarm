import { DataTableToolbar } from '@/components/data-table/DataTableToolbar';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import type {
  ListTagsResponse,
  TagResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsTagsListTagsEndpoint } from '@/lib/api/generated/tags/tags';

interface PartFilterBarProps {
  selectedTags: string[];
  onToggle: (tag: string) => void;
}

/** Tag filter chips wired to GET /tags — clicking a chip toggles it in/out of the
 * PartsPage's `tag` query param (US5, AND-semantics across selected tags). */
export function PartFilterBar({ selectedTags, onToggle }: PartFilterBarProps) {
  const tagsQuery = useOpenFarmApiEndpointsTagsListTagsEndpoint();
  const tagsData = tagsQuery.data?.data as ListTagsResponse | undefined;
  const tags: TagResponse[] = tagsData?.tags ?? [];

  if (tags.length === 0) {
    return null;
  }

  return (
    <DataTableToolbar>
      <ToggleGroup
        type="multiple"
        variant="outline"
        aria-label="Filter parts by tags"
        value={selectedTags}
        onValueChange={(next) => {
          const changed = [...new Set([...selectedTags, ...next])].find(
            (tag) => selectedTags.includes(tag) !== next.includes(tag),
          );
          if (changed) onToggle(changed);
        }}
        className="max-w-full flex-wrap justify-start"
      >
        {tags
          .filter((tag) => tag.name)
          .map((tag) => (
            <ToggleGroupItem key={tag.id} value={tag.name ?? ''}>
              {tag.name}
            </ToggleGroupItem>
          ))}
      </ToggleGroup>
    </DataTableToolbar>
  );
}
