import type { ReactNode } from 'react';
import { Link } from 'react-router';
import type { LibraryKind } from './api';
import { useLibraryLocation } from './useLibraryLocation';

export function LibraryItemLink({
  library,
  id,
  children,
}: {
  library: LibraryKind;
  id?: string;
  children: ReactNode;
}) {
  const { itemLocation } = useLibraryLocation(library);
  return (
    <Link draggable={false} to={itemLocation(id ?? '')} className="font-medium">
      {children}
    </Link>
  );
}
