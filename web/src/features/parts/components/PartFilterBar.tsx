import { Badge } from '@/components/ui/badge';
import type {
  ListTagsResponse,
  TagResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsTagsListTagsEndpoint } from '@/lib/api/generated/tags/tags';
import { cn } from '@/lib/utils';

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
    <div className="flex flex-wrap gap-2">
      {tags.map((tag) => {
        const isSelected = tag.name ? selectedTags.includes(tag.name) : false;
        return (
          <button
            key={tag.id}
            type="button"
            onClick={() => tag.name && onToggle(tag.name)}
          >
            <Badge
              variant={isSelected ? 'default' : 'outline'}
              className={cn(
                'cursor-pointer font-normal',
                isSelected && 'ring-2 ring-primary/30',
              )}
            >
              {tag.name}
            </Badge>
          </button>
        );
      })}
    </div>
  );
}
