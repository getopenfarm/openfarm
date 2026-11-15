import { describe, expect, it } from 'vitest';
import type { PrintJobSendProgress } from '@/features/production-runs/hooks/sendProgress';
import { printerStatusToBadge } from './printerStatusBadge';

const uploading: PrintJobSendProgress = {
  printJobId: 'j',
  printerId: 'p',
  stage: 'Uploading',
  percent: 42,
  attempt: 1,
  error: null,
};

describe('printerStatusToBadge', () => {
  it('shows the send on an Idle printer', () => {
    expect(printerStatusToBadge('Idle', true, uploading)).toEqual({
      kind: 'blue',
      label: 'Sending 42%',
    });
  });

  it('lets the printer own state win once it leaves Idle', () => {
    expect(printerStatusToBadge('Printing', true, uploading).label).toBe(
      'Printing',
    );
  });

  it('says Needs setup on an Idle printer that cannot take any job yet', () => {
    expect(
      printerStatusToBadge('Idle', true, undefined, ['Select a build plate']),
    ).toEqual({
      kind: 'amber',
      label: 'Needs setup',
      solid: true,
      setup: true,
    });
  });

  it('is solid only where the operator must act', () => {
    expect(printerStatusToBadge('AwaitingClear', true)).toMatchObject({
      kind: 'amber',
      label: 'Not ready',
      solid: true,
    });
    for (const badge of [
      printerStatusToBadge('Idle', true),
      printerStatusToBadge('Printing', true),
      printerStatusToBadge('Idle', false),
      printerStatusToBadge('Idle', true, uploading),
    ])
      expect(badge.solid).toBeFalsy();
  });

  it('keeps the real state of a printer that is busy, and Offline, even when setup is missing', () => {
    expect(
      printerStatusToBadge('Printing', true, undefined, [
        'Select a build plate',
      ]).label,
    ).toBe('Printing');
    expect(
      printerStatusToBadge('Idle', false, undefined, ['Select a build plate'])
        .label,
    ).toBe('Offline');
  });

  it('shows Ready when nothing is missing', () => {
    expect(printerStatusToBadge('Idle', true, undefined, []).label).toBe(
      'Ready',
    );
  });

  it('shows Offline before any send', () => {
    expect(printerStatusToBadge('Idle', false, uploading).label).toBe(
      'Offline',
    );
  });
});
