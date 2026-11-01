import { z } from 'zod';
import type {
  MaintenanceTaskResponse,
  SaveMaintenanceTaskRequest,
} from '@/lib/api/generated/endpoints.schemas';
import { startOfDayIso, todayInput } from './maintenance';

// Numeric inputs stay strings in the form; blank = not set (intervals) or 0 (carries).
const blankOr = (test: (n: number) => boolean, message: string) =>
  z
    .string()
    .trim()
    .refine((v) => v === '' || test(Number(v)), message);
const positive = blankOr((n) => n > 0, 'Must be > 0.');
const positiveWhole = blankOr(
  (n) => Number.isInteger(n) && n > 0,
  'Must be a whole number > 0.',
);

/** Usage before tracking, shared with Set last done. */
export const carryFields = {
  carryHours: blankOr((n) => n >= 0, 'Must be ≥ 0.'),
  carryPrints: blankOr(
    (n) => Number.isInteger(n) && n >= 0,
    'Must be a whole number ≥ 0.',
  ),
};

export const maintenanceTaskSchema = z
  .object({
    name: z
      .string()
      .trim()
      .min(1, 'Name is required.')
      .max(120, 'Max 120 characters.'),
    everyPrintHours: positive,
    everyDays: positiveWhole,
    everyPrints: positiveWhole,
    warnAtPercent: z
      .string()
      .trim()
      .refine((v) => {
        const n = Number(v);
        return v !== '' && Number.isInteger(n) && n >= 1 && n <= 99;
      }, 'Must be 1–99.'),
    printerIds: z.array(z.string()),
    lastDone: z
      .string()
      .refine((v) => v === '' || v <= todayInput(), 'Cannot be in the future.'),
    ...carryFields,
    notes: z.string().max(2000, 'Max 2000 characters.'),
  })
  .refine((v) => v.everyPrintHours || v.everyDays || v.everyPrints, {
    path: ['everyPrintHours'],
    message: 'Set at least one interval.',
  });

export type MaintenanceTaskFormValues = z.infer<typeof maintenanceTaskSchema>;

export function formValues(
  task?: MaintenanceTaskResponse,
): MaintenanceTaskFormValues {
  const text = (n?: number | null) => (n == null ? '' : String(n));
  return {
    name: task?.name ?? '',
    everyPrintHours: text(task?.everyPrintHours),
    everyDays: text(task?.everyDays),
    everyPrints: text(task?.everyPrints),
    warnAtPercent: text(task?.warnAtPercent ?? 80),
    printerIds: task?.printerIds ?? [],
    lastDone: '',
    carryHours: '',
    carryPrints: '',
    notes: task?.notes ?? '',
  };
}

const optional = (v: string) => (v === '' ? null : Number(v));

/** Blank last done = now (server); a date = local start of that day. */
export function baselinePayload(v: MaintenanceTaskFormValues) {
  return {
    lastDoneAtUtc: v.lastDone ? startOfDayIso(v.lastDone) : null,
    carryHours: Number(v.carryHours || 0),
    carryPrints: Number(v.carryPrints || 0),
  };
}

export function taskPayload(
  v: MaintenanceTaskFormValues,
): SaveMaintenanceTaskRequest {
  return {
    name: v.name.trim(),
    notes: v.notes.trim() || null,
    everyPrintHours: optional(v.everyPrintHours),
    everyDays: optional(v.everyDays),
    everyPrints: optional(v.everyPrints),
    warnAtPercent: Number(v.warnAtPercent),
    printerIds: v.printerIds,
    ...baselinePayload(v),
  };
}
