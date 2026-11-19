import {
  type PrinterStatus,
  STATUS_META,
} from '@/components/printer-tile/status';
import type {
  AuditEntryResponse,
  AuditFieldChange,
} from '@/lib/api/generated/endpoints.schemas';

const TYPE_LABELS: Record<string, string> = {
  Printer: 'Printer',
  PrintJob: 'Print job',
  ExternalPrint: 'Printer-started print',
  ProductionRun: 'Production run',
  ProductionDemand: 'Production demand',
  Part: 'Part',
  Assembly: 'Assembly',
  Spool: 'Spool',
  Filament: 'Filament',
  FilamentVariant: 'Filament color',
  FeedUnit: 'Feed unit',
  MachineProfile: 'Machine profile',
  FilamentProfile: 'Filament profile',
  ProcessProfile: 'Process profile',
  MaintenanceTask: 'Maintenance task',
  MaintenanceLog: 'Maintenance log',
  MaintenanceEnrollment: 'Maintenance enrollment',
  NonPrintedComponent: 'Cost item',
  User: 'User',
  Group: 'Group',
  AccessToken: 'Access token',
  ConnectedApp: 'Connected app',
  BrowserSession: 'Session',
  LibraryFolder: 'Library folder',
  FarmSettings: 'Farm settings',
  ServerSettings: 'Server settings',
  AuthenticationSettingsRecord: 'Authentication settings',
  AuthenticationSettingsChanged: 'Authentication settings changed',
  OidcBackchannelLogout: 'Signed out by identity provider',
  PrinterModelCost: 'Printer model costs',
  FeeProfile: 'Fee profile',
  BomLine: 'BOM line',
};

export function humanize(value?: string | null) {
  if (!value) return 'Unknown';
  if (TYPE_LABELS[value]) return TYPE_LABELS[value];
  const text = value
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replaceAll('_', ' ')
    .toLowerCase()
    .replace(/\bip\b/g, 'IP')
    .replace(/\bid\b/g, 'ID')
    .replace(/\buuid\b/g, 'UUID')
    .replace(/\brfid\b/g, 'RFID')
    .replace(/\bapi\b/g, 'API')
    .replace(/\butc\b/g, 'UTC')
    .replace(/\bjson\b/g, 'JSON');
  return text.charAt(0).toUpperCase() + text.slice(1);
}

export function auditFieldLabel(field?: string) {
  if (!field) return 'Field';
  const parts = field.split('.');
  if (parts.length < 3) return humanize(field);
  const [kind, id] = parts;
  const group = kind.startsWith('Identity')
    ? kind.startsWith('IdentityUserRole')
      ? 'Group membership'
      : 'Permission'
    : humanize(kind);
  return `${group} · ${humanize(parts.at(-1))}${/^[\da-f]{8}-/i.test(id) ? ` · ${id.slice(0, 8)}` : ''}`;
}

export function auditActor(entry: AuditEntryResponse) {
  return (
    entry.actorName ??
    (entry.channel === 'System' ? 'System' : 'Unknown historical user')
  );
}
export function isAgent(entry: AuditEntryResponse) {
  return entry.channel === 'Token' || entry.channel === 'App';
}
export function auditActorLine(entry: AuditEntryResponse) {
  const actor = auditActor(entry);
  if (entry.channel === 'System') {
    const owner = entry.details?.initiatedByName ?? entry.actorName;
    return owner ? `System, for ${owner}` : 'System';
  }
  if (entry.channel === 'Token')
    return `${actor} via token “${entry.credentialLabel ?? 'unknown token'}”`;
  if (entry.channel === 'App')
    return `${actor} via ${entry.credentialLabel ?? 'a connected app'} (app)`;
  return actor;
}

export function auditValue(value: unknown): string {
  if (value == null || value === '') return '—';
  if (typeof value === 'object') return JSON.stringify(value, null, 2);
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  return String(value);
}

export function auditFieldValue(
  recordType: string | undefined,
  field: string | undefined,
  value: unknown,
) {
  if (
    typeof value === 'string' &&
    recordType === 'Printer' &&
    field === 'Status' &&
    Object.hasOwn(STATUS_META, value)
  )
    return STATUS_META[value as PrinterStatus].label;
  if (
    typeof value === 'string' &&
    [
      'Status',
      'State',
      'BuildPlateType',
      'AssignmentMode',
      'QuantityBasis',
      'Mode',
      'Source',
    ].includes(field ?? '')
  )
    return humanize(value);
  return auditValue(value);
}

export function auditSetChanges(change: AuditFieldChange) {
  if (!Array.isArray(change.before) || !Array.isArray(change.after))
    return null;
  const before = change.before.map(auditValue);
  const after = change.after.map(auditValue);
  return {
    added: after.filter((v) => !before.includes(v)),
    removed: before.filter((v) => !after.includes(v)),
  };
}

function shortValue(value: unknown, recordType?: string, field?: string) {
  const text =
    typeof value === 'object' && value !== null
      ? JSON.stringify(value)
      : auditFieldValue(recordType, field, value);
  return text.length > 120 ? `${text.slice(0, 117)}…` : text;
}
export function auditSummary(entry: AuditEntryResponse, limit = 2) {
  if (!entry.changes?.length) return humanize(entry.eventType);
  const shown = entry.changes.slice(0, limit).map((change) => {
    const label = auditFieldLabel(change.field);
    const set = auditSetChanges(change);
    if (set)
      return `${label}: ${[...set.added.map((v) => `+${v}`), ...set.removed.map((v) => `−${v}`)].join(', ') || 'reordered'}`;
    return `${label} ${shortValue(change.before, entry.entityType, change.field)} → ${shortValue(change.after, entry.entityType, change.field)}`;
  });
  const more = entry.changes.length - shown.length;
  return shown.join('; ') + (more > 0 ? `; and ${more} more` : '');
}

export const CHANNEL_LABELS: Record<string, string> = {
  Browser: 'Signed in',
  Token: 'Access token',
  App: 'Connected app',
  System: 'System',
  Agents: 'Tokens and apps',
};
export const ACTION_LABELS: Record<string, string> = {
  create: 'Created',
  update: 'Changed',
  delete: 'Removed',
  action: 'Action',
};
export function auditActionKind(action?: string | null) {
  return action === 'create'
    ? 'green'
    : action === 'delete'
      ? 'red'
      : action === 'update'
        ? 'blue'
        : 'zinc';
}
