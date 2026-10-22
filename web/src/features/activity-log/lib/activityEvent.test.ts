import { describe, expect, it } from 'vitest';
import { describeActivityEvent } from './activityEvent';

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
});
