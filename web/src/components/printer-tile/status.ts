export type PrinterStatus = 'Idle' | 'Printing' | 'AwaitingClear';

interface StatusMeta {
  label: string;
  textClass: string;
  dotClass: string;
}

export const STATUS_META: Record<PrinterStatus, StatusMeta> = {
  Idle: {
    label: 'Idle',
    textClass: 'text-status-idle',
    dotClass: 'bg-status-idle',
  },
  Printing: {
    label: 'Printing',
    textClass: 'text-status-printing',
    dotClass: 'bg-status-printing',
  },
  AwaitingClear: {
    label: 'Awaiting Clear',
    textClass: 'text-status-attention',
    dotClass: 'bg-status-attention',
  },
};

// Network reachability is tracked separately from PrinterStatus (never conflated) — see
// spec.md User Story 5.
export const OFFLINE_META = {
  label: 'Offline',
  textClass: 'text-status-offline',
  dotClass: 'bg-status-offline',
};
