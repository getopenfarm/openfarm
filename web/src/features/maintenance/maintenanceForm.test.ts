import { describe, expect, it } from 'vitest';
import { progressCaption, startOfDayIso } from './maintenance';
import {
  formValues,
  maintenanceTaskSchema,
  taskPayload,
} from './maintenanceForm';

const valid = { ...formValues(), name: 'Lubricate rods', everyDays: '30' };

describe('maintenanceTaskSchema', () => {
  it('needs a name and at least one interval', () => {
    const result = maintenanceTaskSchema.safeParse(formValues());
    expect(result.success).toBe(false);
    const paths = result.error?.issues.map((i) => i.path.join('.'));
    expect(paths).toContain('name');
    expect(paths).toContain('everyPrintHours');
    expect(maintenanceTaskSchema.safeParse(valid).success).toBe(true);
  });

  it('rejects bad numbers and future last done', () => {
    const bad = (patch: Partial<typeof valid>) =>
      maintenanceTaskSchema.safeParse({ ...valid, ...patch }).success;
    expect(bad({ everyDays: '0' })).toBe(false);
    expect(bad({ everyPrints: '2.5' })).toBe(false);
    expect(bad({ everyPrintHours: '-1' })).toBe(false);
    expect(bad({ warnAtPercent: '100' })).toBe(false);
    expect(bad({ carryPrints: '1.5' })).toBe(false);
    expect(bad({ lastDone: '2999-01-01' })).toBe(false);
    expect(bad({ everyPrintHours: '0.5', carryHours: '12.5' })).toBe(true);
  });
});

describe('taskPayload', () => {
  it('sends blanks as null or 0 and last done as local start of day', () => {
    const payload = taskPayload({
      ...valid,
      name: '  Lubricate rods ',
      printerIds: ['p1'],
      lastDone: '2026-10-05',
      carryPrints: '3',
    });
    expect(payload).toMatchObject({
      name: 'Lubricate rods',
      notes: null,
      everyPrintHours: null,
      everyDays: 30,
      everyPrints: null,
      warnAtPercent: 80,
      printerIds: ['p1'],
      carryHours: 0,
      carryPrints: 3,
    });
    const at = new Date(payload.lastDoneAtUtc ?? '');
    expect([at.getFullYear(), at.getMonth(), at.getDate()]).toEqual([
      2026, 9, 5,
    ]);
    expect([at.getHours(), at.getMinutes()]).toEqual([0, 0]);
    expect(payload.lastDoneAtUtc).toBe(startOfDayIso('2026-10-05'));
    expect(taskPayload(valid).lastDoneAtUtc).toBeNull();
  });
});

describe('progressCaption', () => {
  it('shows overdue by, left, and a lower bound for untracked hours', () => {
    expect(
      progressCaption({
        drivingTrigger: 'Hours',
        used: 332,
        interval: 320,
        remaining: -12,
        untrackedHours: 1,
      }),
    ).toBe('≥ 332 / 320 h · Overdue by 12 h');
    expect(
      progressCaption({
        drivingTrigger: 'Prints',
        used: 3,
        interval: 50,
        remaining: 47,
      }),
    ).toBe('3 / 50 prints · 47 prints left');
    expect(
      progressCaption({
        drivingTrigger: 'Days',
        used: 25.55,
        interval: 30,
        remaining: 4.45,
      }),
    ).toBe('25.6 / 30 d · 4.5 d left');
  });
});
