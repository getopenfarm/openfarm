import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import type {
  FolderPage as ApiFolderPage,
  AssemblySummaryResponse,
  LibraryContentsResponse,
  LibraryEntryResponse,
  LibraryFolder,
  PartSummaryResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { httpClient } from '@/lib/api/httpClient';

export type LibraryKind = 'parts' | 'assemblies';
export interface Folder {
  id: string;
  name: string;
  parentId: string | null;
  library: string;
}
export interface FolderPage {
  items: Folder[];
  ancestors: Folder[];
  totalCount: number;
}
export function useFolders(
  library: LibraryKind,
  parentId: string | null,
  page = 1,
  pageSize = 25,
  enabled = true,
) {
  return useQuery({
    queryKey: ['/api/v1/library', library, parentId, page, pageSize],
    enabled,
    queryFn: async ({ signal }): Promise<FolderPage> => {
      const response = (
        await httpClient<{ data: ApiFolderPage }>(
          `/api/v1/library/${library}/folders?${new URLSearchParams({ ...(parentId ? { parentId } : {}), page: String(page), pageSize: String(pageSize) })}`,
          { signal },
        )
      ).data;
      return {
        items: (response.items ?? []).map((folder) =>
          normalizeFolder(folder, library),
        ),
        ancestors: (response.ancestors ?? []).map((folder) =>
          normalizeFolder(folder, library),
        ),
        totalCount: response.totalCount ?? 0,
      };
    },
  });
}
export function useLibraryMutation(library: LibraryKind) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({
      path,
      method,
      body,
    }: {
      path: string;
      method: string;
      body?: unknown;
    }) =>
      httpClient(`/api/v1/library/${library}/${path}`, {
        method,
        ...(body ? { body: JSON.stringify(body) } : {}),
      }),
    onSuccess: async () => {
      await Promise.all([
        client.invalidateQueries({ queryKey: ['/api/v1/library', library] }),
        client.invalidateQueries({
          predicate: (q) =>
            String(q.queryKey[0]).startsWith(`/api/v1/${library}`),
        }),
      ]);
    },
  });
}

export type LibraryEntry =
  | { kind: 'folder'; id: string; name: string; folder: Folder }
  | { kind: 'part'; id: string; name: string; part: PartSummaryResponse }
  | {
      kind: 'assembly';
      id: string;
      name: string;
      assembly: AssemblySummaryResponse;
    };
export type PartLibraryRow = PartSummaryResponse & { libraryFolder?: Folder };
export type AssemblyLibraryRow = AssemblySummaryResponse & {
  libraryFolder?: Folder;
};
export interface ContentsPage {
  items: LibraryEntry[];
  totalCount: number;
  ancestors: Folder[];
}
export function useLibraryContents(
  library: LibraryKind,
  params: {
    parentId?: string;
    page: number;
    pageSize: number;
    sortBy?: string;
    sortDirection?: string;
    includePreviews: boolean;
  },
) {
  return useQuery<ContentsPage>({
    queryKey: ['/api/v1/library', library, 'contents', params],
    placeholderData: keepPreviousData,
    queryFn: async ({ signal }): Promise<ContentsPage> => {
      const query = new URLSearchParams(
        Object.entries(params)
          .filter(([, v]) => v !== undefined)
          .map(([k, v]) => [k, String(v)]),
      );
      const response = (
        await httpClient<{ data: LibraryContentsResponse }>(
          `/api/v1/library/${library}/contents?${query}`,
          { signal },
        )
      ).data;
      return {
        items: (response.items ?? []).map((entry) =>
          normalizeEntry(entry, library),
        ),
        totalCount: response.totalCount ?? 0,
        ancestors: (response.ancestors ?? []).map((folder) =>
          normalizeFolder(folder, library),
        ),
      };
    },
  });
}
export const entryRowId = (entry: LibraryEntry) =>
  entry.kind === 'folder' ? `folder:${entry.id}` : entry.id;

/** Part edits can also change descendant previews of assemblies. */
export function invalidateLibraryContents(
  client: ReturnType<typeof useQueryClient>,
) {
  return client.invalidateQueries({ queryKey: ['/api/v1/library'] });
}

function normalizeFolder(folder: LibraryFolder, library: LibraryKind): Folder {
  if (!folder.id) throw new Error('Folder response is missing its ID.');
  return {
    id: folder.id,
    name: folder.name ?? 'Folder',
    library: folder.library ?? library,
    parentId: folder.parentId ?? null,
  };
}
function normalizeEntry(
  entry: LibraryEntryResponse,
  library: LibraryKind,
): LibraryEntry {
  if (!entry.id || entry.name === undefined)
    throw new Error('Library entry is missing its ID or name.');
  const base = { id: entry.id, name: entry.name };
  if (entry.kind === 'folder' && entry.folder)
    return {
      ...base,
      kind: 'folder',
      folder: normalizeFolder(entry.folder, library),
    };
  if (entry.kind === 'part' && entry.part)
    return { ...base, kind: 'part', part: entry.part };
  if (entry.kind === 'assembly' && entry.assembly)
    return { ...base, kind: 'assembly', assembly: entry.assembly };
  throw new Error('Library entry has an unsupported type or missing details.');
}
