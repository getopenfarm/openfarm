/**
 * Server messages end with "Configure in /printers/<id>/edit." so a client can find the fix; where the fix is shown as a
 * link of its own the sentence is only noise.
 */
export function stripConfigurationLink(message: string): string {
  return message.replace(
    /\s*Configure (?:in|profiles at) \/(?:filament|printers)\/[a-fA-F0-9-]+\/edit\.?/g,
    '',
  );
}
