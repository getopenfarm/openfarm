import { describe, expect, it } from 'vitest';
import {
  auditActorLine,
  auditFieldLabel,
  auditFieldValue,
  auditSummary,
  auditValue,
  humanize,
} from './auditFormat';

describe('Audit presentation', () => {
  it('summarizes actual values instead of just naming fields', () => {
    expect(
      auditSummary({
        changes: [
          { field: 'Name', before: 'Before', after: 'After' },
          { field: 'BuildPlateType', before: 'Smooth', after: 'Textured' },
          { field: 'IsEnabled', before: false, after: true },
        ],
      }),
    ).toBe(
      'Name Before → After; Build plate type Smooth → Textured; and 1 more',
    );
  });
  it('names record types, fields and asynchronous actors in readable terms', () => {
    expect(auditFieldValue('Printer', 'Status', 'Idle')).toBe('Ready');
    expect(auditFieldValue('Printer', 'Status', 'AwaitingClear')).toBe(
      'Not ready',
    );
    expect(auditFieldValue('PrintJob', 'Status', 'Printing')).toBe('Printing');
    expect(humanize('MachineProfile')).toBe('Machine profile');
    expect(
      auditFieldLabel(
        'PartFilamentRequirement.12345678-abcd-0000-0000-000000000000.MaterialType',
      ),
    ).toBe('Part filament requirement · Material type · 12345678');
    expect(
      auditActorLine({
        channel: 'Token',
        actorName: 'Alex',
        credentialLabel: 'Farm assistant',
      }),
    ).toBe('Alex via token “Farm assistant”');
    expect(
      auditActorLine({
        channel: 'System',
        details: { initiatedByName: 'Alex' },
      }),
    ).toBe('System, for Alex');
  });
  it('distinguishes missing, false and zero values and summarizes set changes', () => {
    expect(auditValue(null)).toBe('—');
    expect(auditValue(false)).toBe('No');
    expect(auditValue(0)).toBe('0');
    expect(
      auditSummary({
        changes: [
          {
            field: 'Permissions',
            before: ['printers:list'],
            after: ['spools:list'],
          },
        ],
      }),
    ).toBe('Permissions: +spools:list, −printers:list');
  });
});
