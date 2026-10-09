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

export function reportedStateName(state: string): string {
  return LABELS[state.trim().toUpperCase()] ?? state;
}

export function reportedStateLabel(state: string): string {
  const label = LABELS[state.trim().toUpperCase()];
  return label ? `${label} (${state})` : state;
}
