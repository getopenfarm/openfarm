const LABELS: Record<string, string> = {
  IDLE: 'Idle',
  INIT: 'Initializing',
  PREPARE: 'Preparing',
  SLICING: 'Slicing',
  RUNNING: 'Printing',
  PAUSE: 'Paused',
  FINISH: 'Finished',
  FAILED: 'Failed',
};

export function reportedStateLabel(state: string): string {
  const label = LABELS[state.trim().toUpperCase()];
  return label ? `${label} (${state})` : state;
}
