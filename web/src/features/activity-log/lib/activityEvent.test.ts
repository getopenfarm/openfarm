import { describe, expect, it } from 'vitest';
import { activityEventKind, describeActivityEvent } from './activityEvent';

describe('describeActivityEvent', () => {
  it('names the event once and keeps acronyms upper case', () => {
    expect(describeActivityEvent('Spool', 'SpoolLoadedFromRfid')).toBe(
      'Spool loaded from RFID',
    );
    expect(describeActivityEvent('Spool', 'SpoolRfidLinked')).toBe(
      'Spool RFID linked',
    );
    expect(describeActivityEvent('Printer', 'ConnectionEdited')).toBe(
      'Printer connection edited',
    );
  });

  it('names maintenance entries on the printer timeline', () => {
    expect(describeActivityEvent('Printer', 'MaintenanceCompleted')).toBe(
      'Maintenance done',
    );
    expect(describeActivityEvent('Printer', 'MaintenanceRebaselined')).toBe(
      'Maintenance last done set',
    );
    expect(
      describeActivityEvent('MaintenanceTask', 'MaintenanceTaskCreated'),
    ).toBe('Maintenance task created');
    expect(activityEventKind('MaintenanceCompleted')).toBe('green');
  });

  it('names and colors connected app events', () => {
    expect(describeActivityEvent('ConnectedApp', 'ConnectedAppAdded')).toBe(
      'Connected app added',
    );
    expect(describeActivityEvent('ConnectedApp', 'ConnectedAppRevoked')).toBe(
      'Connected app revoked',
    );
    expect(
      describeActivityEvent('ConnectedApp', 'ConnectedAppCredentialReuse'),
    ).toBe('Connected app credential reuse');
    expect(activityEventKind('ConnectedAppAdded')).toBe('green');
    expect(activityEventKind('ConnectedAppRevoked')).toBe('red');
    expect(activityEventKind('ConnectedAppCredentialReuse')).toBe('zinc');
  });
});
