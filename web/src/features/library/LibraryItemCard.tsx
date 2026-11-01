import type { Row } from '@tanstack/react-table';
import { flexRender } from '@tanstack/react-table';
import type { ReactNode } from 'react';
import { Link } from 'react-router';
import {
  Card,
  CardAction,
  CardContent,
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
  disabled,
  fileTypeChip,
  materialChip,
}: {
  row: Row<T>;
  name: string;
  to: string;
  images: string[];
  assembly?: boolean;
  disabled: boolean;
  fileTypeChip?: ReactNode;
  materialChip?: ReactNode;
}) {
  const { folderId } = useLibraryLocation(assembly ? 'assemblies' : 'parts');
  const detailLocation = folderId
    ? `${to}?folder=${encodeURIComponent(folderId)}`
    : to;
  function cell(id: string) {
    const c = row.getAllCells().find((c) => c.column.id === id);
    return c ? flexRender(c.column.columnDef.cell, c.getContext()) : null;
  }
  const fields = row
    .getVisibleCells()
    .filter(
      (c) =>
        c.column.getCanHide() &&
        (assembly
          ? ['description', 'lineCount']
          : ['nozzleDiameter', 'estimate']
        ).includes(c.column.id),
    );
  return (
    <Card
      size="sm"
      className="h-full transition-colors group-hover/library-tile:bg-muted/50 group-focus-within/library-tile:bg-muted/50"
    >
      <CardHeader>
        <div className="flex min-w-0 items-center gap-2">
          <LibraryDragHandle name={name} />
          <CardTitle className="min-w-0">
            <Link
              draggable={false}
              to={detailLocation}
              onClick={(e) => {
                if (disabled) e.preventDefault();
              }}
              aria-disabled={disabled || undefined}
              className="line-clamp-2"
            >
              {name}
            </Link>
          </CardTitle>
        </div>
        <CardAction>{cell('actions')}</CardAction>
      </CardHeader>
      <CardContent className="flex flex-col gap-2">
        <div className="h-32">
          <Link
            draggable={false}
            tabIndex={-1}
            aria-hidden="true"
            to={detailLocation}
            onClick={(e) => {
              if (disabled) e.preventDefault();
            }}
            className="flex h-full items-center justify-center overflow-hidden rounded-md bg-muted"
          >
            <LibraryPreview
              images={images}
              kind={assembly ? 'assembly' : 'part'}
            />
          </Link>
        </div>
        {(fileTypeChip || materialChip) && (
          <div className="flex min-w-0 items-center gap-1">
            {materialChip && <div className="min-w-0">{materialChip}</div>}
            {fileTypeChip && (
              <div className="ml-auto shrink-0">{fileTypeChip}</div>
            )}
          </div>
        )}
      </CardContent>

      {fields.length > 0 && (
        <CardContent>
          <dl className="grid min-w-0 grid-cols-2 gap-2">
            {fields.map((field) => (
              <div key={field.id} className="min-w-0">
                <dt className="text-xs text-muted-foreground">
                  {field.column.columnDef.meta?.label ??
                    (typeof field.column.columnDef.header === 'string'
                      ? field.column.columnDef.header
                      : field.column.id)}
                </dt>
                <dd className="min-w-0 wrap-break-word">
                  {flexRender(field.column.columnDef.cell, field.getContext())}
                </dd>
              </div>
            ))}
          </dl>
        </CardContent>
      )}
    </Card>
  );
}
