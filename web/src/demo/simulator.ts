// Makes the demo farm feel live: prints progress (30× real time) and finish, cleared printers take the next
// matching queued plate through the real send stages, and the UI hears it as it would from the server.
import { queryClient } from '@/lib/api/queryClient';
import { actions, eligible, jobById, partById } from './api';
import { jobs, type Printer, printers, spools } from './farm';
import { emit } from './signalr';

const TICK_MS = 2000;
const AUTO_CLEAR_MS = 60_000;
const finishedAt = new Map<string, number>();
let paused = false;

/** Refetch what a farm change touches; forms and other pages keep their state. */
export function refresh() {
  const prefixes = [
    '/api/v1/printers',
    '/api/v1/print-jobs',
    '/api/v1/production-runs',
    '/api/v1/notifications',
    '/api/v1/spools',
    '/api/v1/filaments',
  ];
  void queryClient.invalidateQueries({
    predicate: (query) => {
      const key = query.queryKey[0];
      return (
        typeof key === 'string' &&
        prefixes.some((prefix) => key.startsWith(prefix))
      );
    },
  });
}

const minutesFor = (printer: Printer) =>
  partById(jobById(printer.jobId)?.partId)?.minutesPerPlate ?? 60;
const statusChanged = (printer: Printer) =>
  emit('PrinterStatusChanged', {
    printerId: printer.id,
    status: printer.status,
    isReachable: printer.reachable,
  });
const jobChanged = (jobId: string) =>
  emit('PrintJobStatusChanged', { printJobId: jobId });

export function finish(printer: Printer) {
  const job = jobById(printer.jobId);
  if (job) {
    job.status = 'Completed';
    job.endedAt = new Date().toISOString();
    jobChanged(job.id);
  }
  printer.status = 'AwaitingClear';
  printer.reportedState = 'FINISH';
  printer.progress = 100;
  printer.remainingMinutes = 0;
  finishedAt.set(printer.id, Date.now());
  statusChanged(printer);
  refresh();
}

function markReady(printer: Printer) {
  printer.status = 'Idle';
  printer.reportedState = 'IDLE';
  printer.jobId = null;
  printer.progress = 0;
  finishedAt.delete(printer.id);
  statusChanged(printer);
  refresh();
  setTimeout(() => dispatchNext(printer), 900);
}

function dispatchNext(printer: Printer) {
  if (printer.status !== 'Idle') return;
  const next = jobs
    .filter(
      (j) => j.status === 'Queued' && !j.printerId && j.source === 'Dispatched',
    )
    .sort((a, b) =>
      a.mode === b.mode
        ? (a.position ?? 0) - (b.position ?? 0)
        : a.mode === 'Finite'
          ? -1
          : 1,
    )
    .find((j) => eligible(j, printer));
  if (!next) return;
  next.printerId = printer.id;
  const send = (stage: string, percent: number | null = null) =>
    emit('PrintJobSendProgress', {
      printJobId: next.id,
      printerId: printer.id,
      stage,
      percent,
      attempt: 1,
      error: null,
    });
  send('Slicing');
  refresh();
  const steps: [number, () => void][] = [
    [1800, () => send('Uploading', 18)],
    [2300, () => send('Uploading', 46)],
    [2800, () => send('Uploading', 81)],
    [3300, () => send('Uploading', 100)],
    [3700, () => send('Starting')],
    [
      5000,
      () => {
        next.status = 'Printing';
        next.position = null;
        next.startedAt = new Date().toISOString();
        printer.status = 'Printing';
        printer.reportedState = 'RUNNING';
        printer.jobId = next.id;
        printer.progress = 0;
        printer.remainingMinutes = minutesFor(printer);
        printer.nozzleTemp = 220;
        printer.bedTemp = 55;
        send('Ended');
        statusChanged(printer);
        jobChanged(next.id);
        refresh();
      },
    ],
  ];
  for (const [delay, step] of steps) setTimeout(step, delay);
}

function tick() {
  if (paused || document.hidden) return;
  for (const printer of printers) {
    if (printer.status === 'Printing' && printer.reportedState === 'RUNNING') {
      const total = minutesFor(printer);
      printer.remainingMinutes = Math.max(0, printer.remainingMinutes - 1);
      printer.progress = Math.min(
        100,
        100 * (1 - printer.remainingMinutes / total),
      );
      printer.nozzleTemp = 219 + Math.round(Math.random() * 2);
      printer.bedTemp =
        (printer.modelCode === 'A1' ? 65 : 55) + (Math.random() < 0.3 ? 1 : 0);
      const loaded = spools.find((s) =>
        printer.feed.some((u) =>
          u.slots.some((slot) => slot.active && slot.spoolId === s.id),
        ),
      );
      const grams =
        partById(jobById(printer.jobId)?.partId)?.gramsPerPlate ?? 0;
      if (loaded)
        loaded.remaining = Math.max(0, loaded.remaining - grams / total);
      if (printer.remainingMinutes === 0) finish(printer);
    } else if (printer.status !== 'Printing') {
      printer.nozzleTemp = Math.max(27, printer.nozzleTemp - 9);
      printer.bedTemp = Math.max(26, printer.bedTemp - 2);
    }
    // Another operator clears beds the visitor leaves; the printer that starts awaiting clear stays for the tour.
    const since = finishedAt.get(printer.id);
    if (
      printer.status === 'AwaitingClear' &&
      since &&
      Date.now() - since > AUTO_CLEAR_MS
    )
      markReady(printer);
  }
  refresh();
}

/** The printer to clear first: one already awaiting clear, or the print nearest its end, finished now. */
export function printerToClear() {
  const waiting = printers.find(
    (p) => p.status === 'AwaitingClear' && p.reachable,
  );
  if (waiting) return waiting;
  const soonest = printers
    .filter((p) => p.status === 'Printing' && p.reportedState === 'RUNNING')
    .sort((a, b) => a.remainingMinutes - b.remainingMinutes)[0];
  if (soonest) finish(soonest);
  return soonest;
}

export function setPaused(value: boolean) {
  paused = value;
}

export function startSimulator() {
  for (const printer of printers)
    if (printer.status === 'Printing')
      printer.remainingMinutes = Math.round(
        minutesFor(printer) * (1 - printer.progress / 100),
      );
  actions.markReady = markReady;
  actions.changed = refresh;
  actions.remote = (job, action) => {
    const printer = printers.find((p) => p.id === job.printerId);
    if (!printer) return;
    if (action === 'cancel') {
      job.status = 'Cancelled';
      job.endedAt = new Date().toISOString();
      printer.status = 'AwaitingClear';
      printer.reportedState = 'FAILED';
      finishedAt.set(printer.id, Date.now());
    } else {
      job.status = action === 'pause' ? 'Paused' : 'Printing';
      printer.reportedState = action === 'pause' ? 'PAUSE' : 'RUNNING';
    }
    statusChanged(printer);
    jobChanged(job.id);
    refresh();
  };
  setInterval(tick, TICK_MS);
}
