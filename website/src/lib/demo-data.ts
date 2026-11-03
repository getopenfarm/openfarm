export const demoViews = [
  { id: 'dashboard', label: 'Dashboard', caption: 'See your fleet', hint: 'Try marking the P1S ready, or open Queue to see why its next job is waiting.' },
  { id: 'queue', label: 'Queue', caption: 'Resolve a waiting job', hint: 'Open the base job diagnosis, then mark its printer ready to see the sample job start.' },
  { id: 'runs', label: 'Runs', caption: 'Plan complete sets', hint: 'Move the sets slider. Watch component quantities and planned whole plates change together.' },
  { id: 'filament', label: 'Filament', caption: 'Track every spool', hint: 'Choose a spool to see its remaining material and the printer where it is loaded.' },
] as const;

export const demoSpools = [
  { id: 'black', name: 'PLA · Black', color: '#27272a', remaining: 740, printer: 'P1S · AMS slot 1' },
  { id: 'blue', name: 'PLA · Blue', color: '#2563eb', remaining: 420, printer: 'A1 · External spool' },
  { id: 'orange', name: 'PETG · Orange', color: '#ea580c', remaining: 880, printer: 'U1 · Loaded spool' },
] as const;
