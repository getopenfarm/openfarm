import { useSearchParams } from 'react-router';
import type { LibraryKind } from './api';

export function useLibraryLocation(library: LibraryKind) {
  const [params] = useSearchParams();
  const folderId = params.get('folder');
  const base = `/${library}`;
  const location = folderId
    ? `${base}?folder=${encodeURIComponent(folderId)}`
    : base;
  return {
    itemLocation: (id: string) =>
      `${base}/${id}${folderId ? `?folder=${encodeURIComponent(folderId)}` : ''}`,
    folderId,
    location,
    addLocation: `${base}/new${folderId ? `?folder=${encodeURIComponent(folderId)}` : ''}`,
  };
}
