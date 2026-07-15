// Mock data for the Profiles page — no backend exists for slicer profiles yet
// (see specs/003-slicer-profile-import/spec.md for the real import feature this
// stands in for). Kept intentionally close to that spec's shape: Machine and
// Filament profiles are two separate catalogs, and a profile may one day carry
// a `baseProfileId` reference to a base profile it overrides (unpopulated here,
// but the field exists so this type doesn't fight that future model).

export type ProfileCategory = 'Machine' | 'Filament';

export type ProfileSource = 'Bambu Studio' | 'OrcaSlicer' | 'PrusaSlicer';

export interface Profile {
  id: string;
  name: string;
  category: ProfileCategory;
  /** Nozzle diameter in millimeters — the dimension a Machine profile targets,
   * or the dimension a Filament preset's settings were tuned for. */
  nozzleSizeMm: number;
  source: ProfileSource;
  /** Reserved for a future base/override relationship (see spec 003). Never
   * populated by this mock data, but present so consumers don't assume every
   * profile is a standalone, globally unique record. */
  baseProfileId?: string;
  // Filament-only fields — absent (undefined) on Machine profiles.
  nozzleTempC?: number;
  bedTempC?: number;
  maxVolumetricSpeedMm3s?: number;
  fanPercent?: number;
}

export const SEED_PROFILES: Profile[] = [
  // --- Machine profiles ---
  {
    id: 'machine-bambu-x1-carbon-04',
    name: 'Bambu X1 Carbon 0.4 nozzle',
    category: 'Machine',
    nozzleSizeMm: 0.4,
    source: 'Bambu Studio',
  },
  {
    id: 'machine-bambu-p1s-04',
    name: 'Bambu P1S 0.4 nozzle',
    category: 'Machine',
    nozzleSizeMm: 0.4,
    source: 'Bambu Studio',
  },
  {
    id: 'machine-prusa-mk4s-04',
    name: 'Prusa MK4S 0.4 nozzle',
    category: 'Machine',
    nozzleSizeMm: 0.4,
    source: 'PrusaSlicer',
  },

  // --- Filament profiles ---
  {
    id: 'filament-bambu-pla-basic-x1c-04',
    name: 'Bambu PLA Basic @ Bambu X1 Carbon',
    category: 'Filament',
    nozzleSizeMm: 0.4,
    nozzleTempC: 220,
    bedTempC: 55,
    maxVolumetricSpeedMm3s: 15,
    fanPercent: 100,
    source: 'Bambu Studio',
  },
  {
    id: 'filament-bambu-pla-basic-x1c-06',
    name: 'Bambu PLA Basic @ Bambu X1 Carbon',
    category: 'Filament',
    nozzleSizeMm: 0.6,
    nozzleTempC: 225,
    bedTempC: 55,
    maxVolumetricSpeedMm3s: 23,
    fanPercent: 100,
    source: 'Bambu Studio',
  },
  {
    id: 'filament-bambu-pla-matte-x1c-04',
    name: 'Bambu PLA Matte @ Bambu X1 Carbon',
    category: 'Filament',
    nozzleSizeMm: 0.4,
    nozzleTempC: 215,
    bedTempC: 50,
    maxVolumetricSpeedMm3s: 12,
    fanPercent: 100,
    source: 'Bambu Studio',
  },
  {
    id: 'filament-bambu-pla-matte-x1c-06',
    name: 'Bambu PLA Matte @ Bambu X1 Carbon',
    category: 'Filament',
    nozzleSizeMm: 0.6,
    nozzleTempC: 220,
    bedTempC: 50,
    maxVolumetricSpeedMm3s: 20,
    fanPercent: 100,
    source: 'Bambu Studio',
  },
  {
    id: 'filament-bambu-petg-hf-x1c-04',
    name: 'Bambu PETG HF @ Bambu X1 Carbon',
    category: 'Filament',
    nozzleSizeMm: 0.4,
    nozzleTempC: 255,
    bedTempC: 70,
    maxVolumetricSpeedMm3s: 18,
    fanPercent: 50,
    source: 'Bambu Studio',
  },
  {
    id: 'filament-bambu-petg-hf-x1c-06',
    name: 'Bambu PETG HF @ Bambu X1 Carbon',
    category: 'Filament',
    nozzleSizeMm: 0.6,
    nozzleTempC: 260,
    bedTempC: 70,
    maxVolumetricSpeedMm3s: 26,
    fanPercent: 50,
    source: 'Bambu Studio',
  },
  {
    id: 'filament-bambu-asa-x1c-04',
    name: 'Bambu ASA @ Bambu X1 Carbon',
    category: 'Filament',
    nozzleSizeMm: 0.4,
    nozzleTempC: 260,
    bedTempC: 90,
    maxVolumetricSpeedMm3s: 16,
    fanPercent: 0,
    source: 'Bambu Studio',
  },
  {
    id: 'filament-bambu-asa-x1c-06',
    name: 'Bambu ASA @ Bambu X1 Carbon',
    category: 'Filament',
    nozzleSizeMm: 0.6,
    nozzleTempC: 265,
    bedTempC: 90,
    maxVolumetricSpeedMm3s: 24,
    fanPercent: 0,
    source: 'Bambu Studio',
  },
  {
    id: 'filament-generic-pla-orca-04',
    name: 'Generic PLA',
    category: 'Filament',
    nozzleSizeMm: 0.4,
    nozzleTempC: 210,
    bedTempC: 55,
    maxVolumetricSpeedMm3s: 13,
    fanPercent: 100,
    source: 'OrcaSlicer',
  },
  {
    id: 'filament-generic-pla-orca-06',
    name: 'Generic PLA',
    category: 'Filament',
    nozzleSizeMm: 0.6,
    nozzleTempC: 215,
    bedTempC: 55,
    maxVolumetricSpeedMm3s: 21,
    fanPercent: 100,
    source: 'OrcaSlicer',
  },
  {
    id: 'filament-orca-pla-high-speed-04',
    name: 'OrcaSlicer PLA High Speed',
    category: 'Filament',
    nozzleSizeMm: 0.4,
    nozzleTempC: 225,
    bedTempC: 55,
    maxVolumetricSpeedMm3s: 20,
    fanPercent: 100,
    source: 'OrcaSlicer',
  },
  {
    id: 'filament-generic-petg-orca-04',
    name: 'Generic PETG',
    category: 'Filament',
    nozzleSizeMm: 0.4,
    nozzleTempC: 250,
    bedTempC: 75,
    maxVolumetricSpeedMm3s: 16,
    fanPercent: 40,
    source: 'OrcaSlicer',
  },
  {
    id: 'filament-generic-petg-orca-06',
    name: 'Generic PETG',
    category: 'Filament',
    nozzleSizeMm: 0.6,
    nozzleTempC: 255,
    bedTempC: 75,
    maxVolumetricSpeedMm3s: 24,
    fanPercent: 40,
    source: 'OrcaSlicer',
  },
  {
    id: 'filament-generic-tpu-orca-04',
    name: 'Generic TPU',
    category: 'Filament',
    nozzleSizeMm: 0.4,
    nozzleTempC: 230,
    bedTempC: 45,
    maxVolumetricSpeedMm3s: 8,
    fanPercent: 80,
    source: 'OrcaSlicer',
  },
];
