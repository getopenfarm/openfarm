import type { Preferences } from './types';

export const DEFAULT_PREFERENCES: Preferences = {
  libraryFields: {},
  listViews: {},
  listDensities: {},
};

export const DEFAULT_VIEW_MODE = 'table';

export const DEFAULT_DENSITY_MODE = 'medium';

export const PREFERENCES_STORAGE_KEY = 'openfarm.preferences.v1';
