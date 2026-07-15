// Mock data for the Maintenance page — no backend maintenance-tracking domain
// exists yet (see CLAUDE.md: Phase 1 fleet management is live, but scheduled
// maintenance tracking hasn't been built). The "needs attention" flags below
// are fabricated, but cross-referenced onto the real fleet via
// `matchMaintenanceToFleet` so at least the printer identity shown is real.

import type { MaintenanceFlagSeed } from '../lib/matchMaintenanceToFleet';

export const MAINTENANCE_FLAG_SEEDS: MaintenanceFlagSeed[] = [
  {
    pattern: 'X1C',
    task: 'Belt tension check',
    detailText: 'overdue by 2 days',
    severity: 'overdue',
  },
  {
    pattern: 'X1C',
    task: 'Nozzle inspection',
    detailText: 'due in 20 print hours',
    severity: 'due',
  },
  {
    pattern: 'X1C',
    task: 'Lubrication',
    detailText: 'due in 40 print hours',
    severity: 'due',
  },
  {
    pattern: 'MK4',
    task: 'Nozzle check',
    detailText: 'due soon',
    severity: 'due',
  },
];

export type TaskTemplateDueKind = 'overdue' | 'due' | 'ok';

export interface TaskTemplate {
  task: string;
  trigger: string;
  appliesTo: string;
  lastPerformed: string;
  nextDueText: string;
  nextDueKind: TaskTemplateDueKind;
}

export const TASK_TEMPLATES: TaskTemplate[] = [
  {
    task: 'Nozzle inspection',
    trigger: 'Every 320 print hours',
    appliesTo: 'All Bambu X1C',
    lastPerformed: '12 days ago',
    nextDueText: 'in 20h',
    nextDueKind: 'due',
  },
  {
    task: 'Belt tension check',
    trigger: 'Every 60 days',
    appliesTo: 'All printers',
    lastPerformed: '2 months ago',
    nextDueText: 'overdue 2d',
    nextDueKind: 'overdue',
  },
  {
    task: 'Lubrication',
    trigger: 'Every 480 print hours',
    appliesTo: 'X1/P1 series',
    lastPerformed: '20 days ago',
    nextDueText: 'in 40h',
    nextDueKind: 'due',
  },
  {
    task: 'Firmware update',
    trigger: 'Every 90 days',
    appliesTo: 'All printers',
    lastPerformed: '1 month ago',
    nextDueText: 'in 34d',
    nextDueKind: 'ok',
  },
  {
    task: 'Full clean',
    trigger: 'Every 200 jobs',
    appliesTo: 'All printers',
    lastPerformed: '—',
    nextDueText: 'in 60 jobs',
    nextDueKind: 'ok',
  },
];
