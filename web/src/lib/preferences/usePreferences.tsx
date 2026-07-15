import { createContext, useContext, useEffect, useMemo, useState } from 'react';
import {
  DEFAULT_PREFERENCES,
  DEFAULT_VIEW_MODE,
  PREFERENCES_STORAGE_KEY,
} from './defaultPreferences';
import type { Density, ListPageKey, Preferences, ViewMode } from './types';

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
  setDensity: (density: Density) => void;
  getListView: (page: ListPageKey) => ViewMode;
  setListView: (page: ListPageKey, mode: ViewMode) => void;
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

  useEffect(() => {
    document.documentElement.dataset.density = preferences.density;
  }, [preferences.density]);

  const value = useMemo<PreferencesContextValue>(
    () => ({
      preferences,
      setDensity: (density) => setPreferences((prev) => ({ ...prev, density })),
      getListView: (page) => preferences.listViews[page] ?? DEFAULT_VIEW_MODE,
      setListView: (page, mode) =>
        setPreferences((prev) => ({
          ...prev,
          listViews: { ...prev.listViews, [page]: mode },
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
