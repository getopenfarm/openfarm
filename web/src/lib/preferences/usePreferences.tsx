import { createContext, useContext, useEffect, useMemo, useState } from 'react';
import {
  DEFAULT_DENSITY_MODE,
  DEFAULT_PREFERENCES,
  DEFAULT_VIEW_MODE,
  PREFERENCES_STORAGE_KEY,
} from './defaultPreferences';
import type { DensityMode, ListPageKey, Preferences, ViewMode } from './types';

function loadPreferences(): Preferences {
  if (typeof window === 'undefined') return DEFAULT_PREFERENCES;
  try {
    const raw = window.localStorage.getItem(PREFERENCES_STORAGE_KEY);
    if (!raw) return DEFAULT_PREFERENCES;
    const parsed = JSON.parse(raw) as Partial<Preferences>;
    return {
      ...DEFAULT_PREFERENCES,
      ...parsed,
      listViews: { ...DEFAULT_PREFERENCES.listViews, ...parsed.listViews },
      listDensities: {
        ...DEFAULT_PREFERENCES.listDensities,
        ...parsed.listDensities,
      },
    };
  } catch {
    return DEFAULT_PREFERENCES;
  }
}

function savePreferences(preferences: Preferences) {
  if (typeof window === 'undefined') return;
  try {
    window.localStorage.setItem(
      PREFERENCES_STORAGE_KEY,
      JSON.stringify(preferences),
    );
  } catch {
    // storage disabled (private browsing, quota) — preference just won't persist
  }
}

interface PreferencesContextValue {
  preferences: Preferences;
  getListView: (page: ListPageKey) => ViewMode;
  setListView: (page: ListPageKey, mode: ViewMode) => void;
  getListDensity: (page: ListPageKey) => DensityMode;
  setListDensity: (page: ListPageKey, mode: DensityMode) => void;
}

const PreferencesContext = createContext<PreferencesContextValue | null>(null);

export function PreferencesProvider({
  children,
}: {
  children: React.ReactNode;
}) {
  const [preferences, setPreferences] = useState<Preferences>(loadPreferences);

  useEffect(() => {
    savePreferences(preferences);
  }, [preferences]);

  const value = useMemo<PreferencesContextValue>(
    () => ({
      preferences,
      getListView: (page) => preferences.listViews[page] ?? DEFAULT_VIEW_MODE,
      setListView: (page, mode) =>
        setPreferences((prev) => ({
          ...prev,
          listViews: { ...prev.listViews, [page]: mode },
        })),
      getListDensity: (page) =>
        preferences.listDensities[page] ?? DEFAULT_DENSITY_MODE,
      setListDensity: (page, mode) =>
        setPreferences((prev) => ({
          ...prev,
          listDensities: { ...prev.listDensities, [page]: mode },
        })),
    }),
    [preferences],
  );

  return (
    <PreferencesContext.Provider value={value}>
      {children}
    </PreferencesContext.Provider>
  );
}

export function usePreferences(): PreferencesContextValue {
  const ctx = useContext(PreferencesContext);
  if (!ctx) {
    throw new Error('usePreferences must be used within a PreferencesProvider');
  }
  return ctx;
}
