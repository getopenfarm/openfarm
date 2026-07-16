import { useSyncExternalStore } from 'react';

type Updater<T> = T[] | ((prev: T[]) => T[]);

export function createMockStore<T>(initial: T[]) {
  let state = initial;
  const listeners = new Set<() => void>();

  const notify = () => {
    for (const listener of listeners) listener();
  };

  const subscribe = (listener: () => void) => {
    listeners.add(listener);
    return () => listeners.delete(listener);
  };

  const getSnapshot = () => state;

  const setState = (updater: Updater<T>) => {
    state =
      typeof updater === 'function'
        ? (updater as (prev: T[]) => T[])(state)
        : updater;
    notify();
  };

  const reset = () => setState(initial);

  const useStore = () => {
    const snapshot = useSyncExternalStore(subscribe, getSnapshot);
    return [snapshot, setState] as const;
  };

  return { useStore, setState, getSnapshot, reset };
}

export type MockStore<T> = ReturnType<typeof createMockStore<T>>;
