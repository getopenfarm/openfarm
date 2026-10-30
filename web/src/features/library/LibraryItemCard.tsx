import type { Row } from '@tanstack/react-table';
import { flexRender } from '@tanstack/react-table';
import type { ReactNode } from 'react';
import { Link } from 'react-router';
import {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { LibraryDragHandle } from './LibraryDnd';
import { LibraryPreview } from './LibraryPreview';
import { useLibraryLocation } from './useLibraryLocation';

export function LibraryItemCard<T>({
  row,
  name,
  to,
  images,
  assembly,
  description,
  children,
  disabled,
}: {
  row: Row<T>;
  name: string;
  to: string;
  images: string[];
  assembly?: boolean;
  description?: string | null;
  children?: ReactNode;
  disabled: boolean;
}) {
  const { folderId } = useLibraryLocation(assembly ? 'assemblies' : 'parts');
  const detailLocation = folderId
    ? `${to}?folder=${encodeURIComponent(folderId)}`
    : to;
  function cell(id: string) {
    const c = row.getAllCells().find((c) => c.column.id === id);
    return c ? flexRender(c.column.columnDef.cell, c.getContext()) : null;
  }
  return (
    <Card size="sm" className="h-full">
      <CardHeader>
        <div className="flex min-w-0 items-center gap-2">
          <LibraryDragHandle name={name} />
          {cell('select')}
          <CardTitle className="min-w-0">
            <Link
              draggable={false}
              to={detailLocation}
              onClick={(e) => {
                if (disabled) e.preventDefault();
              }}
              aria-disabled={disabled || undefined}
              className="line-clamp-2 hover:underline"
            >
              {name}
            </Link>
          </CardTitle>
        </div>
        <CardAction>{cell('actions')}</CardAction>
        {description && (
          <CardDescription className="line-clamp-2">
            {description}
          </CardDescription>
        )}
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        <Link
          draggable={false}
          tabIndex={-1}
          aria-hidden="true"
          to={detailLocation}
          onClick={(e) => {
            if (disabled) e.preventDefault();
          }}
          className="flex aspect-[4/3] items-center justify-center overflow-hidden rounded-md bg-muted"
        >
          <LibraryPreview
            images={images}
            kind={assembly ? 'assembly' : 'part'}
          />
        </Link>
        {children}
      </CardContent>
    </Card>
  );
}
