import type { ReactNode } from 'react';
import { Badge } from '@/components/ui/badge';
import {
  CardAction,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { pluralize } from '@/lib/pluralize';

/** One wrapping section header for draft editors, with the add action aligned to the right. */
export function EditorSectionHeader({
  title,
  titleId,
  description,
  count,
  action,
}: {
  title: string;
  titleId?: string;
  description?: string;
  count: number;
  action: ReactNode;
}) {
  return (
    <CardHeader className="flex flex-wrap items-start justify-between gap-3">
      <div className="flex min-w-0 flex-1 basis-48 flex-col gap-1">
        <div className="flex flex-wrap items-center gap-2">
          <CardTitle>
            <h3 id={titleId}>{title}</h3>
          </CardTitle>
          <Badge variant="secondary">{pluralize(count, 'item')}</Badge>
        </div>
        {description && <CardDescription>{description}</CardDescription>}
      </div>
      <CardAction className="ml-auto max-w-full">{action}</CardAction>
    </CardHeader>
  );
}
