import { keepPreviousData } from '@tanstack/react-query';
import type { OnChangeFn, SortingState } from '@tanstack/react-table';
import { useCallback, useEffect, useRef, useState } from 'react';

export const serverListQueryOptions = { placeholderData: keepPreviousData };

/** One state transition resets the page together with the requested ordering. */
export function useServerListState(
  columnId: string,
  field: string,
  alphabetical = false,
) {
  const [state, setState] = useState({
    page: 1,
    pageSize: 25,
    sorting: (alphabetical
      ? [{ id: columnId, desc: false }]
      : []) as SortingState,
  });
  const onSortingChange: OnChangeFn<SortingState> = useCallback((updater) => {
    setState((previous) => ({
      ...previous,
      page: 1,
      sorting:
        typeof updater === 'function' ? updater(previous.sorting) : updater,
    }));
  }, []);
  const setPage = useCallback(
    (page: number) => setState((previous) => ({ ...previous, page })),
    [],
  );
  const setPageSize = useCallback(
    (pageSize: number) =>
      setState((previous) => ({ ...previous, pageSize, page: 1 })),
    [],
  );
  const active = state.sorting[0];
  return {
    ...state,
    setPage,
    setPageSize,
    params: {
      page: state.page,
      pageSize: state.pageSize,
      ...(active
        ? { sortBy: field, sortDirection: active.desc ? 'desc' : 'asc' }
        : {}),
    },
    serverSorting: {
      sorting: state.sorting,
      onSortingChange,
      enableSortingRemoval: !alphabetical,
    },
  };
}

export function useServerPageRecovery(
  page: number,
  pageSize: number,
  totalCount: number,
  settled: boolean,
  onPageChange: (page: number) => void,
) {
  useEffect(() => {
    if (!settled) return;
    const lastPage = Math.max(1, Math.ceil(totalCount / pageSize));
    if (page > lastPage) onPageChange(lastPage);
  }, [page, pageSize, totalCount, settled, onPageChange]);
}

export function useRetainedListData<T>(data: T | undefined): T | undefined {
  const previous = useRef(data);
  useEffect(() => {
    if (data !== undefined) previous.current = data;
  }, [data]);
  return data ?? previous.current;
}
