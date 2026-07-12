export type PrinterStatus =
  | 'printing'
  | 'attention'
  | 'complete'
  | 'idle'
  | 'offline';

interface StatusMeta {
  label: string;
  textClass: string;
  dotClass: string;
}

export const STATUS_META: Record<PrinterStatus, StatusMeta> = {
  printing: {
    label: 'Printing',
    textClass: 'text-status-printing',
    dotClass: 'bg-status-printing',
  },
  attention: {
    label: 'Attention',
    textClass: 'text-status-attention',
    dotClass: 'bg-status-attention',
  },
  complete: {
    label: 'Complete',
    textClass: 'text-status-complete',
    dotClass: 'bg-status-complete',
  },
  idle: {
    label: 'Idle',
    textClass: 'text-status-idle',
    dotClass: 'bg-status-idle',
  },
  offline: {
    label: 'Offline',
    textClass: 'text-status-offline',
    dotClass: 'bg-status-offline',
  },
};
