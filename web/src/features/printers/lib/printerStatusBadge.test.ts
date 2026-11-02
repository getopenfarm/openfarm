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

  it('shows Offline before any send', () => {
    expect(printerStatusToBadge('Idle', false, uploading).label).toBe(
      'Offline',
    );
  });
});
