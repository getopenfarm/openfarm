// The demo's in-browser API: install.ts routes every /api/v1 request here. Responses use the generated contract
// types, built from the in-memory farm (farm.ts); the simulator changes that state.
import type {
  AccountResponse,
  ActivityLogPageResponse,
  AssemblyResponse,
  AssemblySummaryResponse,
  ConnectedAppResponse,
  ConnectedAppsResponse,
  ExplodeAssemblyResponse,
  FeedReadingResponse,
  FeedUnitResponse,
  FilamentResponse,
  FleetPrinterResponse,
  GroupResponse,
  JobMatchingDiagnostics,
  LibraryContentsResponse,
  LibraryEntryResponse,
  ListFilamentsResponse,
  ListNotificationsResponse,
  ListQueueJobsResponse,
  MaintenanceEnrollmentResponse,
  MaintenanceTaskResponse,
  MatchingCheck,
  PartResponse,
  PartSummaryResponse,
  PlateCountsResponse,
  PrintEstimate,
  PrinterDetailResponse,
  PrinterMatchingDiagnostics,
  PrinterModelCostResponse,
  PrinterModelResponse,
  PrintJobResponse,
  ProductionDemandResponse,
  ProductionPlan,
  ProductionRunDetailResponse,
  ProductionRunSummaryResponse,
  QueueJobRowResponse,
  QuoteAssemblyRequest,
  RunLineSummaryResponse,
  SpoolHistoryEntryResponse,
  SpoolResponse,
  StatsDay,
  StatsReport,
  VariantStockResponse,
  WhoAmIResult,
} from '@/lib/api/generated/endpoints.schemas';
import cameraP1s from './assets/camera-p1s.jpg';
import cameraU1 from './assets/camera-u1.jpg';
import {
  type Assembly,
  assemblies,
  currentUser,
  daysAgo,
  filaments,
  folders,
  id,
  type Job,
  jobs,
  minutesAgo,
  now,
  type Part,
  type Printer,
  parts,
  printers,
  type Requirement,
  type Run,
  runs,
  spools,
  variant,
  vendors,
} from './farm';
import { demoQuote } from './quote/quote';

// --- shared lookups ------------------------------------------------------------------------------------------
export const printerById = (printerId: string | null | undefined) =>
  printers.find((p) => p.id === printerId);
export const partById = (partId: string | null | undefined) =>
  parts.find((p) => p.id === partId);
export const jobById = (jobId: string | null | undefined) =>
  jobs.find((j) => j.id === jobId);
const runById = (runId: string | null | undefined) =>
  runs.find((r) => r.id === runId);
const assemblyById = (assemblyId: string | null | undefined) =>
  assemblies.find((a) => a.id === assemblyId);
const spoolById = (spoolId: string | null | undefined) =>
  spools.find((s) => s.id === spoolId);

const MODELS: Record<Printer['modelCode'], PrinterModelResponse> = {
  P1S: bambu('P1S', 'Bambu Lab P1S', true),
  X1C: bambu('X1C', 'Bambu Lab X1 Carbon', false),
  A1: bambu('A1', 'Bambu Lab A1', true),
  A1MINI: bambu('A1MINI', 'Bambu Lab A1 mini', true),
  U1: {
    modelCode: 'U1',
    name: 'Snapmaker U1',
    driver: 'Moonraker',
    extruders: ['T1', 'T2', 'T3', 'T4'],
    connection: {
      serialRequired: false,
      secretName: 'API key',
      secretRequired: false,
      secretHint: 'Moonraker. Blank = none.',
      note: null,
    },
    cameraSupported: true,
  },
};
function bambu(
  modelCode: string,
  name: string,
  cameraSupported: boolean,
): PrinterModelResponse {
  return {
    modelCode,
    name,
    driver: 'Bambu LAN',
    extruders: ['Nozzle'],
    connection: {
      serialRequired: true,
      secretName: 'access code',
      secretRequired: true,
      secretHint: 'Printer screen → Settings → Network.',
      note: 'LAN mode disconnects Bambu Cloud and Bambu Handy.',
    },
    cameraSupported,
  };
}

const PLATE_LABELS: Record<string, string> = {
  TexturedPei: 'Textured PEI Plate',
  HighTemp: 'High Temp Plate',
};

// --- printers ------------------------------------------------------------------------------------------------
function loadedSpool(printer: Printer, slotIndex = -1) {
  const slots = printer.feed.flatMap((unit) => unit.slots);
  const slot = slotIndex >= 0 ? slots[slotIndex] : slots.find((s) => s.active);
  return spoolById(slot?.spoolId);
}
function stageLabel(printer: Printer) {
  if (printer.status !== 'Printing') return null;
  if (printer.reportedState === 'PAUSE') return 'Paused';
  return printer.progress < 3 ? 'Auto bed leveling' : 'Printing';
}
function telemetry(printer: Printer) {
  const job = jobById(printer.jobId);
  const active = printer.status === 'Printing';
  return {
    reportedState: printer.reportedState,
    stateObservedAtUtc: minutesAgo(1),
    activeAlerts: [],
    nozzleDiameterMm: printer.nozzle,
    nozzleObservedAtUtc: minutesAgo(30),
    nozzleConfirmed: true,
    extruders: MODELS[printer.modelCode].extruders?.map((_, index) => ({
      index,
      nozzleDiameterMm: printer.nozzle,
      nozzleConfirmed: true,
      tempC: index === 0 ? printer.nozzleTemp : active ? 170 : 26,
    })),
    nozzleTempC: printer.nozzleTemp,
    bedTempC: printer.bedTemp,
    printProgressPercent:
      printer.status === 'Idle' ? null : Math.round(printer.progress),
    currentStageLabel: stageLabel(printer),
    remainingTimeMinutes: active ? printer.remainingMinutes : null,
    currentJobName: printer.status === 'Idle' ? null : (job?.name ?? null),
  };
}
function feedReadings(printer: Printer): FeedReadingResponse[] {
  return printer.feed.map((unit) => ({
    kind: unit.kind,
    unitIndex: unit.unitIndex,
    displayLabel: unit.label,
    slots: unit.slots.map((slot) => {
      const loaded = spoolById(slot.spoolId);
      const info = loaded ? variant(loaded.variantId) : null;
      return {
        slotPosition: slot.position,
        extruderIndex: unit.label === 'Toolheads' ? slot.position : 0,
        displayLabel: slot.label,
        empty: !loaded,
        materialType: info?.filament.materialType ?? null,
        colorHex: info?.variant.colorHex.replace('#', '') ?? null,
        subBrand: info ? info.filament.material : null,
        remainPercent: loaded
          ? Math.round((loaded.remaining / loaded.weight) * 100)
          : null,
        active: slot.active,
        rfidId: null,
      };
    }),
  }));
}
export function fleetPrinter(printer: Printer): FleetPrinterResponse {
  const t = telemetry(printer);
  const color = loadedSpool(printer);
  return {
    id: printer.id,
    name: printer.name,
    brand: printer.brand,
    modelCode: printer.modelCode,
    model: MODELS[printer.modelCode],
    ipAddress: printer.ip,
    serialNumber: printer.serial,
    status: printer.status,
    isReachable: printer.reachable,
    printProgressPercent: t.printProgressPercent,
    nozzleDiameterMm: printer.nozzle,
    nozzleObservedAtUtc: t.nozzleObservedAtUtc,
    nozzleConfirmed: true,
    nozzleTempC: printer.nozzleTemp,
    bedTempC: printer.bedTemp,
    currentStageLabel: t.currentStageLabel,
    remainingTimeMinutes: t.remainingTimeMinutes,
    currentJobName: t.currentJobName,
    currentPrintJobId: printer.status === 'Idle' ? null : printer.jobId,
    filamentColorHex: color ? variant(color.variantId).variant.colorHex : null,
  };
}
function printerDetail(printer: Printer): PrinterDetailResponse {
  const plate = printer.modelCode === 'U1' ? 'HighTemp' : 'TexturedPei';
  return {
    id: printer.id,
    name: printer.name,
    brand: printer.brand,
    modelCode: printer.modelCode,
    model: MODELS[printer.modelCode],
    ipAddress: printer.ip,
    serialNumber: printer.serial,
    status: printer.status,
    isReachable: printer.reachable,
    telemetry: telemetry(printer),
    feedReadings: feedReadings(printer),
    machineProfileId: id('3ac1', printers.indexOf(printer) + 1),
    machineProfileName: `${MODELS[printer.modelCode].name} ${printer.nozzle} nozzle`,
    machineProfileIssue: null,
    processProfileId: id('3ac2', 1),
    processProfileName: '0.20mm Standard',
    buildPlateType: plate,
    buildPlateTypeLabel: PLATE_LABELS[plate],
    compatibleBuildPlateTypes: Object.entries(PLATE_LABELS).map(
      ([value, label]) => ({ value, label }),
    ),
  };
}
function feedUnits(printer: Printer): FeedUnitResponse[] {
  return printer.feed.map((unit) => ({
    id: unit.id,
    kind: unit.kind,
    unitIndex: unit.unitIndex,
    displayLabel: unit.label,
    isRevealed: true,
    replacesFeedUnitId: null,
    supersededByFeedUnitId: null,
    archivedAtUtc: null,
    feedingSlotId: null,
    feedingSelectionSource: null,
    feedSlots: unit.slots.map((slot) => {
      const loaded = spoolById(slot.spoolId);
      const info = loaded ? variant(loaded.variantId) : null;
      return {
        id: slot.id,
        slotPosition: slot.position,
        displayLabel: slot.label,
        deactivatedAtUtc: null,
        currentSpool: loaded
          ? {
              id: loaded.id,
              filamentVariantId: loaded.variantId,
              remainingWeightGrams: Math.round(loaded.remaining),
              spoolWeightGrams: loaded.weight,
            }
          : null,
        detectedMaterialType: info?.filament.materialType ?? null,
        detectedColorHex: info?.variant.colorHex.replace('#', '') ?? null,
        detectedSubBrand: info?.filament.material ?? null,
      };
    }),
  }));
}

// --- filament ------------------------------------------------------------------------------------------------
function slotLabel(slotId: string | null) {
  for (const printer of printers)
    for (const unit of printer.feed)
      for (const slot of unit.slots)
        if (slot.id === slotId)
          return `${printer.name} · ${unit.kind === 'ExternalSpool' ? 'External' : `${unit.label} ${slot.label}`}`;
  return null;
}
function spoolResponse(spool: (typeof spools)[number]): SpoolResponse {
  const { filament, variant: v } = variant(spool.variantId);
  return {
    id: spool.id,
    label: null,
    filamentVariantId: v.id,
    filamentId: filament.id,
    colorName: v.colorName,
    colorHex: v.colorHex,
    material: filament.material,
    vendorName: filament.vendorName,
    spoolWeightGrams: spool.weight,
    remainingWeightGrams: Math.round(spool.remaining),
    state: spool.state,
    feedSlotId: spool.slotId,
    feedSlotLabel: slotLabel(spool.slotId),
    hasUsageOverrun: false,
    price: spool.price,
    pricePerKg: filament.pricePerKg,
    supplier: filament.vendorName,
    orderNumber: null,
    receivedOn: spool.receivedOn,
  };
}
const usedLast30 = (variantId: string) =>
  jobs
    .filter(
      (j) =>
        j.status === 'Completed' && partById(j.partId)?.variantId === variantId,
    )
    .reduce((sum, j) => sum + (partById(j.partId)?.gramsPerPlate ?? 0), 0) * 48;
function variantStock(variantId: string): VariantStockResponse {
  const { filament, variant: v } = variant(variantId);
  const live = spools.filter(
    (s) => s.variantId === variantId && s.state !== 'Finished',
  );
  const onHand = live.reduce((sum, s) => sum + s.remaining, 0);
  const used = usedLast30(variantId);
  const daysLeft = used > 0 ? Math.round(onHand / (used / 30)) : null;
  return {
    filamentVariantId: variantId,
    onHandGrams: Math.round(onHand),
    activeSpoolCount: live.length,
    stockValue: Math.round(onHand * filament.pricePerKg) / 1000,
    unpricedSpoolCount: 0,
    usedLast30DaysGrams: used,
    thresholdGrams: v.lowStockGrams ?? null,
    daysLeft,
    thresholdDays: null,
    isLow: v.lowStockGrams !== undefined && onHand < v.lowStockGrams,
  };
}
function filamentResponse(
  filament: (typeof filaments)[number],
): FilamentResponse {
  return {
    id: filament.id,
    vendorId: filament.vendorId,
    vendorName: filament.vendorName,
    material: filament.material,
    materialType: filament.materialType,
    pricePerKg: filament.pricePerKg,
    lowStockGrams: null,
    lowStockDays: null,
    profileAssignments: [],
    variants: filament.variants.map((v) => ({
      id: v.id,
      colorName: v.colorName,
      colorHex: v.colorHex,
      pricePerKg: null,
      lowStockGrams: v.lowStockGrams ?? null,
      lowStockDays: null,
    })),
  };
}

// --- parts and estimates -------------------------------------------------------------------------------------
function estimate(p: Part, plates = 1): PrintEstimate {
  const { filament, variant: v } = variant(p.variantId);
  return {
    time: { value: p.minutesPerPlate * plates, missing: 0 },
    filament: { value: p.gramsPerPlate * plates, missing: 0 },
    approximate: false,
    colors: [
      {
        materialType: filament.materialType,
        colorHex: v.colorHex,
        variantId: v.id,
        grams: p.gramsPerPlate * plates,
        missing: 0,
      },
    ],
  };
}
const estimation = (p: Part) => ({
  source: 'Slicer',
  machineModel: 'Bambu Lab P1S',
  machineProfileName: `Bambu Lab P1S ${p.nozzle} nozzle`,
  processProfileName: '0.20mm Standard @BBL X1C',
  filamentProfileNames: [
    `${variant(p.variantId).filament.vendorName} ${variant(p.variantId).filament.material}`,
  ],
  buildPlate: 'Textured PEI Plate',
  engine: 'BambuStudio',
  version: '2.7.1',
});
function partSummary(p: Part): PartSummaryResponse {
  const { filament, variant: v } = variant(p.variantId);
  return {
    id: p.id,
    name: p.name,
    fileFormat: 'ThreeMf',
    requiresSlicing: false,
    nozzleDiameterMm: p.nozzle,
    printRequirementsStatus: 'Complete',
    processingWarnings: [],
    estimateUnavailableReason: null,
    estimation: estimation(p),
    estimationPrinterBrand: 'Bambu',
    estimationPrinterModelCode: 'P1S',
    printEstimate: estimate(p),
    estimateStatus: 'Available',
    estimatedPrintTimeMinutes: p.minutesPerPlate,
    estimatedFilamentWeightGrams: p.gramsPerPlate,
    unitsPerPrint: p.unitsPerPlate,
    thumbnailBase64: p.thumbnail,
    materialTypes: [filament.materialType],
    tags: p.tags,
    filamentByColor: [
      {
        narrowFilamentVariantId: v.id,
        materialType: filament.materialType,
        colorHex: v.colorHex,
        grams: p.gramsPerPlate,
      },
    ],
  };
}
function partResponse(p: Part): PartResponse {
  const { filament, variant: v } = variant(p.variantId);
  const unitShare = (value: number) =>
    Math.round((value / p.unitsPerPlate) * 10) / 10;
  return {
    ...partSummary(p),
    folderId: p.folderId,
    originalFileName: `${p.name.toLowerCase().replaceAll(' ', '_')}.gcode.3mf`,
    fileSizeBytes: 180_000 + p.gramsPerPlate * 9_000,
    unitEstimate: {
      ...estimate(p),
      time: { value: unitShare(p.minutesPerPlate), missing: 0 },
      filament: { value: unitShare(p.gramsPerPlate), missing: 0 },
    },
    slicer: { engine: 'BambuStudio', version: '2.7.1' },
    filamentRequirements: [
      {
        id: id('9a28', parts.indexOf(p) + 1),
        materialType: filament.materialType,
        detectedColorHex: v.colorHex,
        detectedSubBrand: filament.material,
        narrowFilamentVariantId: v.id,
        narrowFilamentProfileId: null,
        narrowFilamentVariantUnresolved: false,
        narrowFilamentProfileUnresolved: false,
        estimatedGrams: p.gramsPerPlate,
      },
    ],
    plateNumber: 1,
    plateName: null,
    detectedUnitsPerPrint: p.unitsPerPlate,
    quantityDetectionSource: 'Objects',
    quantityNeedsReprocessing: false,
    thumbnails: p.thumbnail
      ? [{ plateIndex: 1, thumbnailBase64: p.thumbnail }]
      : [],
  };
}

// --- jobs and matching ---------------------------------------------------------------------------------------
const loadedFilaments = (printer: Printer) =>
  printer.feed.flatMap((unit) =>
    unit.slots.flatMap((slot) => {
      const loaded = spoolById(slot.spoolId);
      return loaded ? [{ spool: loaded, ...variant(loaded.variantId) }] : [];
    }),
  );
const check = (
  code: string,
  label: string,
  passed: boolean,
  required: string | null,
  actual: string | null,
  configurationUrl: string | null = null,
): MatchingCheck => ({
  code,
  label,
  status: passed ? 'Passed' : 'Failed',
  required,
  actual,
  message: null,
  configurationUrl,
});
export function printerChecks(job: Job, printer: Printer): MatchingCheck[] {
  const p = partById(job.partId);
  const wanted = p ? variant(p.variantId) : null;
  const loaded = loadedFilaments(printer);
  const nozzleMatches = p?.nozzle === printer.nozzle;
  const reserved = jobs.some(
    (other) =>
      other.id !== job.id &&
      other.printerId === printer.id &&
      other.status === 'Queued',
  );
  const url = `/printers/${printer.id}`;
  const checks = [
    check(
      'pin',
      'Assignment target',
      job.assignment !== 'Pinned' || job.pinnedPrinterId === printer.id,
      job.assignment === 'Pinned'
        ? `Pinned to ${printerById(job.pinnedPrinterId)?.name}`
        : 'Any printer',
      printer.name,
    ),
    check(
      'availability',
      'Printer availability',
      printer.status === 'Idle',
      'Idle',
      printer.status,
      url,
    ),
    check(
      'reachability',
      'Connection',
      printer.reachable,
      'Reachable (live printers)',
      printer.reachable ? 'Reachable' : 'Not reachable',
      url,
    ),
    check(
      'reservation',
      'Active job reservation',
      !reserved,
      'No active assigned job',
      reserved
        ? 'Printer has an active assigned job'
        : 'No active assigned job',
      url,
    ),
    check(
      'nozzle-confirmed',
      'Current connection nozzle telemetry',
      true,
      'Confirmed',
      `${printer.nozzle} mm`,
      url,
    ),
    check(
      'nozzle-match',
      'Nozzle diameter',
      nozzleMatches,
      `${p?.nozzle} mm`,
      `${printer.nozzle} mm`,
      url,
    ),
    check(
      'filament',
      'Active loaded filament',
      loaded.some((f) => f.variant.id === wanted?.variant.id),
      wanted
        ? `${wanted.filament.material} · ${wanted.variant.colorName}`
        : 'Any material',
      loaded.length
        ? loaded
            .map(
              (f) =>
                `${f.filament.materialType} · ${f.variant.colorName} (${f.variant.colorHex})`,
            )
            .join(', ')
        : 'No tracked spool on an active feed path',
      url,
    ),
  ];
  checks.push(
    nozzleMatches
      ? check(
          'profiles',
          'Production profile readiness',
          true,
          'Compatible machine and filament assignments; slicing settings when needed',
          'Ready',
        )
      : {
          code: 'profiles',
          label: 'Production profile readiness',
          status: 'NotEvaluated',
          required: null,
          actual: null,
          message:
            'Requires complete part requirements and a matching confirmed nozzle.',
          configurationUrl: null,
        },
  );
  return checks;
}
export const eligible = (job: Job, printer: Printer) =>
  printerChecks(job, printer).every((c) => c.status === 'Passed');

function matching(job: Job): JobMatchingDiagnostics {
  const p = partById(job.partId);
  const wanted = p ? variant(p.variantId) : null;
  const waiting = job.status === 'Queued' && !job.printerId;
  const jobChecks = [
    check('demand-state', 'Demand is enabled', true, 'Enabled', 'Enabled'),
    check(
      'job-state',
      'Job is waiting for assignment',
      waiting,
      'Queued, not dispatched',
      waiting ? 'Queued' : job.printerId ? 'Already dispatched' : job.status,
    ),
    check(
      'part-requirements',
      'Production print requirements',
      true,
      'Complete nozzle and material requirements',
      'Complete',
      `/parts/${job.partId}`,
    ),
  ];
  const results: PrinterMatchingDiagnostics[] = printers.map((printer) => {
    const checks = printerChecks(job, printer);
    const ok = checks.every((c) => c.status === 'Passed');
    return {
      printerId: printer.id,
      printerName: printer.name,
      eligible: ok,
      checks,
      schedulingNote: ok
        ? 'Eligible and next for this printer. Dispatch rechecks live configuration before sending.'
        : null,
    };
  });
  const jobOk = jobChecks.every((c) => c.status === 'Passed');
  const any = results.some((r) => r.eligible);
  const summary = !jobOk
    ? `${jobChecks.find((c) => c.status !== 'Passed')?.label}: ${jobChecks.find((c) => c.status !== 'Passed')?.actual}`
    : any
      ? 'A printer matches. Queue order and final dispatch checks determine when it is sent.'
      : results
          .filter(
            (r) =>
              job.assignment !== 'Pinned' ||
              r.printerId === job.pinnedPrinterId,
          )
          .map(
            (r) =>
              `${r.printerName}: ${r.checks
                ?.filter((c) => c.status === 'Failed')
                .map(
                  (c) =>
                    `${c.label}: requires ${c.required}, actual ${c.actual}`,
                )
                .join('; ')}.`,
          )
          .join(' ');
  return {
    jobId: job.id,
    evaluatedAtUtc: new Date().toISOString(),
    requirementSource: 'Part',
    outcome: any ? 'Eligible' : 'Blocked',
    summary,
    requirements: wanted
      ? [
          {
            materialType: wanted.filament.materialType,
            detectedColorHex: wanted.variant.colorHex,
            selectedVariantId: wanted.variant.id,
            selectedVariantName: `${wanted.filament.material} · ${wanted.variant.colorName}`,
            selectedColorHex: wanted.variant.colorHex,
            profileConstraintId: null,
          },
        ]
      : [],
    jobChecks,
    printers: results,
  };
}

function runLabel(run: Run) {
  if (run.name) return run.name;
  const names = run.lines.map((line) =>
    line.targetKind === 'Part'
      ? (partById(line.targetId)?.name ?? 'Unknown part')
      : (assemblyById(line.targetId)?.name ?? 'Unknown assembly'),
  );
  if (run.lines.length === 1)
    return run.lines[0].quantity === null
      ? names[0]
      : `${names[0]} ×${run.lines[0].quantity}`;
  return `${run.lines.length} lines — ${names.join(', ')}`;
}
function jobRow(job: Job): QueueJobRowResponse {
  const p = partById(job.partId);
  const run = runById(job.runId);
  const pinnedBusy =
    job.assignment === 'Pinned' &&
    printerById(job.pinnedPrinterId)?.status !== 'Idle';
  return {
    jobId: job.id,
    queuePosition: job.position,
    status: job.status,
    assignmentMode: job.assignment,
    pinnedPrinterId: job.pinnedPrinterId,
    dispatchedPrinterId: job.printerId,
    isBlocked: job.status === 'Queued' && !job.printerId && pinnedBusy,
    blockingReason:
      job.status === 'Queued' && pinnedBusy ? matching(job).summary : null,
    sourcePartAvailable: true,
    snapshotState: 'Ready',
    snapshotRecoveryReason: null,
    lastBlockReason: job.lastBlockReason,
    partId: job.source === 'External' ? null : job.partId,
    partName: job.name,
    targetUnits: job.units,
    producedUnits: job.status === 'Completed' ? job.units : 0,
    missingUnits: job.status === 'Failed' ? job.units : 0,
    thumbnailBase64: job.source === 'External' ? null : (p?.thumbnail ?? null),
    plateQuantity: job.units,
    runLineId: job.lineId,
    demandId: job.lineId,
    demandMode: job.mode,
    canRequeue: job.status === 'Failed' || job.status === 'Cancelled',
    productionRunId: job.runId,
    runDisplayLabel: run ? runLabel(run) : '',
    source: job.source,
    startedAtUtc: job.startedAt,
    endedAtUtc: job.endedAt,
  };
}
function jobResponse(job: Job): PrintJobResponse {
  const p = partById(job.partId);
  const wanted = p ? variant(p.variantId) : null;
  return {
    id: job.id,
    canResolveShortfall: false,
    thumbnailBase64: p?.thumbnail ?? null,
    partRequirementId: job.requirementId,
    queuePosition: job.position,
    targetUnits: job.units,
    producedUnits: job.status === 'Completed' ? job.units : 0,
    missingUnits: job.status === 'Failed' ? job.units : 0,
    plateQuantity: job.units,
    assignmentMode: job.assignment,
    pinnedPrinterId: job.pinnedPrinterId,
    dispatchedPrinterId: job.printerId,
    status: job.status,
    fileRevisionHash: '9f2c41d7',
    fileRevisionNumber: 1,
    dispatchedAtUtc: job.startedAt,
    startedAtUtc: job.startedAt,
    endedAtUtc: job.endedAt,
    actualDurationMinutes:
      job.startedAt && job.endedAt
        ? Math.round(
            (Date.parse(job.endedAt) - Date.parse(job.startedAt)) / 60_000,
          )
        : null,
    cancelRequestedAtUtc: null,
    requeuedAsJobId: null,
    writtenOffAtUtc: null,
    filamentConsumption: wanted
      ? [
          {
            id: id('c0c0', jobs.indexOf(job) + 1),
            materialType: wanted.filament.materialType,
            detectedColorHex: wanted.variant.colorHex,
            narrowFilamentVariantId: wanted.variant.id,
            narrowFilamentProfileId: null,
            spoolId: null,
            gramsConsumed: job.status === 'Completed' ? p?.gramsPerPlate : null,
            estimatedGrams: p?.gramsPerPlate,
            toolIndex: 0,
            pricePerKg: wanted.filament.pricePerKg,
            priceSource: 'Filament',
            cost: p
              ? Math.round(p.gramsPerPlate * wanted.filament.pricePerKg) / 1000
              : null,
          },
        ]
      : [],
    createdAtUtc: job.createdAt,
    snapshotState: 'Ready',
    snapshotRecoveryReason: null,
    source: job.source,
    name: job.name,
    lastBlockReason: job.lastBlockReason,
    lastBlockedAtUtc: null,
    provenance: null,
  };
}

// --- runs ----------------------------------------------------------------------------------------------------
function plateCounts(list: Job[]): PlateCountsResponse {
  return {
    completed: list.filter((j) => j.status === 'Completed').length,
    printing: list.filter(
      (j) => j.status === 'Printing' || j.status === 'Paused',
    ).length,
    queued: list.filter((j) => j.status === 'Queued').length,
    needsAction: list.filter((j) => j.status === 'Failed').length,
    writtenOff: 0,
  };
}
const requirementJobs = (requirement: Requirement) =>
  jobs.filter(
    (j) => j.requirementId === requirement.id && j.source === 'Dispatched',
  );
const completedUnits = (list: Job[]) =>
  list
    .filter((j) => j.status === 'Completed')
    .reduce((sum, j) => sum + j.units, 0);
function runSummary(run: Run): ProductionRunSummaryResponse {
  const finite = run.lines
    .flatMap((line) => line.requirements)
    .filter((r) => r.quantity !== null);
  const all = run.lines
    .flatMap((line) => line.requirements)
    .flatMap(requirementJobs);
  const done = completedUnits(all);
  return {
    id: run.id,
    displayLabel: runLabel(run),
    name: run.name,
    status: run.status,
    dueDate: run.dueDate,
    createdAtUtc: run.createdAt,
    lines: run.lines.map((line) => ({
      targetName:
        line.targetKind === 'Part'
          ? partById(line.targetId)?.name
          : assemblyById(line.targetId)?.name,
      quantity: line.quantity,
    })),
    targetUnits: finite.reduce((sum, r) => sum + (r.quantity ?? 0), 0),
    completedUnits: done,
    writtenOffUnits: 0,
    producedUnits: done,
    plates: plateCounts(all),
    blockedCount: 0,
  };
}
function runDetail(run: Run): ProductionRunDetailResponse {
  return {
    id: run.id,
    displayLabel: runLabel(run),
    name: run.name,
    dueDate: run.dueDate,
    notes: run.notes,
    status: run.status,
    createdAtUtc: run.createdAt,
    lines: run.lines.map(
      (line): RunLineSummaryResponse => ({
        quantityBasis: line.quantityBasis,
        id: line.id,
        targetKind: line.targetKind,
        targetId: line.targetId,
        targetName:
          line.targetKind === 'Part'
            ? partById(line.targetId)?.name
            : assemblyById(line.targetId)?.name,
        quantity: line.quantity,
        revisionMode: 'Pinned',
        pinnedPrinterId: null,
        backfillPrinterCap: 1,
        backfillCumulativeUnitCeiling: null,
        cumulativeProducedUnits: completedUnits(
          line.requirements.flatMap(requirementJobs),
        ),
        spansMultipleRevisions: false,
        requirements: line.requirements.map((requirement) => {
          const p = partById(requirement.partId);
          const list = requirementJobs(requirement);
          const queued = list.filter((j) => j.status === 'Queued').length;
          return {
            id: requirement.id,
            partId: requirement.partId,
            partName: p?.name,
            sourcePartAvailable: true,
            quantity: requirement.quantity,
            unitsPerPlate: p?.unitsPerPlate,
            completedUnits: completedUnits(list),
            writtenOffUnits: 0,
            plates: plateCounts(list),
            estimatedMinutesPerPlate: p?.minutesPerPlate,
            estimatedGramsPerPlate: p?.gramsPerPlate,
            remainingEstimate: p ? estimate(p, queued) : null,
            blockingReason: null,
            jobs: list.map((j) => ({
              targetUnits: j.units,
              producedUnits: j.status === 'Completed' ? j.units : 0,
              missingUnits: j.status === 'Failed' ? j.units : 0,
              outputVerified: j.status === 'Completed',
              id: j.id,
              status: j.status,
              printerId: j.printerId,
              startedAtUtc: j.startedAt,
              endedAtUtc: j.endedAt,
              lastBlockReason: j.lastBlockReason,
              requeuedAsJobId: null,
              writtenOff: false,
              needsAction: j.status === 'Failed',
              canRequeue: j.status === 'Failed',
            })),
          };
        }),
      }),
    ),
  };
}
const runOpen = (run: Run) =>
  run.status === 'Active' || run.status === 'Complete';
function plan(
  targetKind: string,
  targetId: string,
  quantity: number,
  basis: string,
): ProductionPlan {
  const lines =
    targetKind === 'Assembly'
      ? (assemblyById(targetId)?.lines ?? []).map((line) => ({
          part: partById(line.partId),
          units: line.quantity * quantity,
        }))
      : [
          {
            part: partById(targetId),
            units:
              basis === 'Plates'
                ? quantity * (partById(targetId)?.unitsPerPlate ?? 1)
                : quantity,
          },
        ];
  const planned = lines.flatMap(({ part: p, units }) => {
    if (!p) return [];
    const plates = Math.ceil(units / p.unitsPerPlate);
    return [{ p, units, plates }];
  });
  const minutes = planned.reduce(
    (sum, x) => sum + x.plates * x.p.minutesPerPlate,
    0,
  );
  const grams = planned.reduce(
    (sum, x) => sum + x.plates * x.p.gramsPerPlate,
    0,
  );
  return {
    version: '1',
    parts: planned.map(({ p, units, plates }) => ({
      partId: p.id,
      name: p.name,
      requiredUnits: units,
      unitsPerPlate: p.unitsPerPlate,
      plates,
      plannedUnits: plates * p.unitsPerPlate,
      surplusUnits: plates * p.unitsPerPlate - units,
      minutes: plates * p.minutesPerPlate,
      grams: plates * p.gramsPerPlate,
      revision: '1',
      updatedAtUtc: p.createdAt,
      jobUnits: Array.from({ length: plates }, (_, i) =>
        Math.min(p.unitsPerPlate, units - i * p.unitsPerPlate),
      ),
      objectMappingVersion: null,
      estimate: estimate(p, plates),
    })),
    plates: planned.reduce((sum, x) => sum + x.plates, 0),
    completeSets: targetKind === 'Assembly' ? quantity : null,
    minutes,
    grams,
    estimate: {
      time: { value: minutes, missing: 0 },
      filament: { value: grams, missing: 0 },
      approximate: false,
      colors: [],
    },
  };
}

// --- library -------------------------------------------------------------------------------------------------
function assemblySummary(a: Assembly): AssemblySummaryResponse {
  return {
    id: a.id,
    name: a.name,
    description: a.description,
    lineCount: a.lines.length,
    previewThumbnails: a.lines
      .flatMap((line) => partById(line.partId)?.thumbnail ?? [])
      .slice(0, 4),
  };
}
function assemblyResponse(a: Assembly): AssemblyResponse {
  return {
    folderId: a.folderId,
    id: a.id,
    name: a.name,
    description: a.description,
    lines: a.lines.map((line, index) => ({
      id: id('b0e1', assemblies.indexOf(a) * 10 + index + 1),
      targetKind: 'Part',
      targetId: line.partId,
      targetName: partById(line.partId)?.name,
      quantity: line.quantity,
    })),
    additionalCosts: [],
    pricing: { mode: 'Markup', value: 150 },
    createdAtUtc: a.createdAt,
    updatedAtUtc: a.createdAt,
  };
}
function libraryContents(
  library: string,
  parentId: string | null,
): LibraryContentsResponse {
  const folderEntries: LibraryEntryResponse[] = folders
    .filter((f) => f.library === library && parentId === null)
    .map((f) => ({
      id: f.id,
      name: f.name,
      kind: 'folder',
      folder: {
        id: f.id,
        library: f.library,
        parentId: null,
        name: f.name,
        createdAtUtc: f.createdAt,
        updatedAtUtc: f.createdAt,
      },
      part: null,
      assembly: null,
      previewThumbnails: parts
        .filter((p) => p.folderId === f.id)
        .flatMap((p) => p.thumbnail ?? [])
        .slice(0, 4),
    }));
  const items: LibraryEntryResponse[] =
    library === 'parts'
      ? parts
          .filter((p) => p.folderId === parentId)
          .map((p) => ({
            id: p.id,
            name: p.name,
            kind: 'part',
            folder: null,
            part: partSummary(p),
            assembly: null,
            previewThumbnails: null,
          }))
      : assemblies
          .filter((a) => a.folderId === parentId)
          .map((a) => ({
            id: a.id,
            name: a.name,
            kind: 'assembly',
            folder: null,
            part: null,
            assembly: assemblySummary(a),
            previewThumbnails: assemblySummary(a).previewThumbnails,
          }));
  const parent = folders.find((f) => f.id === parentId);
  return {
    items: [...folderEntries, ...items],
    totalCount: folderEntries.length + items.length,
    ancestors: parent
      ? [
          {
            id: parent.id,
            library: parent.library,
            parentId: null,
            name: parent.name,
            createdAtUtc: parent.createdAt,
            updatedAtUtc: parent.createdAt,
          },
        ]
      : [],
  };
}

// --- stats ---------------------------------------------------------------------------------------------------
function statsReport(): StatsReport {
  const days: StatsDay[] = Array.from({ length: 30 }, (_, index) => {
    const wave = Math.sin(index / 3) * 4 + Math.cos(index / 7) * 3;
    const completed = Math.max(
      6,
      Math.round(24 + wave + (index % 6 === 5 ? -8 : 0)),
    );
    const failed = index % 4 === 0 ? 2 : index % 3 === 0 ? 1 : 0;
    return {
      day: new Date(now - (29 - index) * 86_400_000).toISOString().slice(0, 10),
      completed,
      failed,
      cancelled: index % 9 === 4 ? 1 : 0,
      usedGrams: completed * 58,
      wasteGrams: failed * 31 + completed * 2,
      units: completed * 4,
      printingHours: completed * 4.4,
      observedHours: 8 * 24,
    };
  });
  const sum = (key: keyof StatsDay) =>
    days.reduce((total, d) => total + Number(d[key] ?? 0), 0);
  const activity = (scale: number) => ({
    printing: Math.round(sum('printingHours') * scale),
    paused: 6 * scale,
    awaitingClear: 41 * scale,
    idle: Math.round((8 * 24 * 30 - sum('printingHours') - 47 - 18) * scale),
    offline: 18 * scale,
    noData: 0,
    observed: 8 * 24 * 30 * scale,
    utilization: (sum('printingHours') * scale) / (8 * 24 * 30 * scale),
  });
  const summary = (scale: number) => {
    const completed = Math.round(sum('completed') * scale);
    const failed = Math.round(sum('failed') * scale * (scale < 1 ? 1.4 : 1));
    const cancelled = Math.round(sum('cancelled') * scale);
    return {
      prints: completed + failed + cancelled,
      completed,
      failed,
      cancelled,
      usedGrams: Math.round(sum('usedGrams') * scale),
      wasteGrams: Math.round(sum('wasteGrams') * scale),
      wasteMachineHours: Math.round(failed * 1.1),
      units: Math.round(sum('units') * scale),
      activity: activity(scale),
      queueWaitP50Minutes: scale < 1 ? 31 : 22,
      queueWaitP95Minutes: scale < 1 ? 140 : 96,
      estimateRatio: 1.03,
      plateClearMinutes: scale < 1 ? 19 : 14,
      successRate: completed / (completed + failed),
      wasteShare: sum('wasteGrams') / sum('usedGrams'),
    };
  };
  return {
    current: summary(1),
    previous: summary(0.88),
    days,
    printers: printers.map((printer, index) => {
      const prints = 70 + ((index * 17) % 40);
      return {
        printerId: printer.id,
        name: printer.name,
        prints,
        completed: prints - (index % 3) - 1,
        failed: (index % 3) + 1,
        cancelled: index % 2,
        usedGrams: prints * 61,
        activity: {
          ...activity(1 / 8),
          utilization: 0.46 + ((index * 7) % 30) / 100,
        },
      };
    }),
    failureCodes: [
      { code: '0300-8003', message: 'Spaghetti detected', prints: 5 },
      { code: '0700-2000', message: 'AMS filament ran out', prints: 3 },
      {
        code: '0500-4001',
        message: 'First layer inspection failed',
        prints: 2,
      },
    ],
  };
}

// --- maintenance ---------------------------------------------------------------------------------------------
const TASKS = [
  {
    n: 1,
    name: 'Clean build plate',
    everyPrints: 50,
    everyPrintHours: null,
    everyDays: null,
  },
  {
    n: 2,
    name: 'Lubricate carbon rods',
    everyPrints: null,
    everyPrintHours: 200,
    everyDays: null,
  },
  {
    n: 3,
    name: 'Clean nozzle wiper',
    everyPrints: null,
    everyPrintHours: null,
    everyDays: 30,
  },
] as const;
// One overdue and one due-soon task; the rest of the fleet is in good shape.
const DUE: Record<string, number> = { 'P1S-02:2': 1.06, 'A1-01:1': 0.88 };
function enrollments(): MaintenanceEnrollmentResponse[] {
  return printers.flatMap((printer, index) =>
    TASKS.map((task) => {
      const interval =
        task.everyPrints ?? task.everyPrintHours ?? task.everyDays ?? 1;
      const used = Math.round(
        interval *
          (DUE[`${printer.name}:${task.n}`] ??
            0.18 + ((index * 3 + task.n * 5) % 11) / 20),
      );
      const remaining = interval - used;
      const state =
        remaining < 0 ? 'Overdue' : used / interval >= 0.85 ? 'DueSoon' : 'Ok';
      return {
        id: id('e0e0', index * 10 + task.n),
        taskId: id('7a5c', task.n),
        taskName: task.name,
        printerId: printer.id,
        printerName: printer.name,
        cycleStartedAtUtc: daysAgo(40),
        lastDoneAtUtc: daysAgo(12 + index),
        carryHours: 0,
        carryPrints: 0,
        everyPrintHours: task.everyPrintHours,
        everyDays: task.everyDays,
        everyPrints: task.everyPrints,
        warnAtPercent: 85,
        hours: task.everyPrintHours ? used : 0,
        prints: task.everyPrints ? used : 0,
        untrackedHours: 0,
        state,
        drivingTrigger: task.everyPrints
          ? 'Prints'
          : task.everyPrintHours
            ? 'Hours'
            : 'Days',
        used,
        interval,
        remaining,
      };
    }),
  );
}
function tasks(): MaintenanceTaskResponse[] {
  const all = enrollments();
  return TASKS.map((task) => {
    const states = all
      .filter((e) => e.taskId === id('7a5c', task.n))
      .map((e) => e.state);
    return {
      id: id('7a5c', task.n),
      name: task.name,
      notes: null,
      everyPrintHours: task.everyPrintHours,
      everyDays: task.everyDays,
      everyPrints: task.everyPrints,
      warnAtPercent: 85,
      createdAtUtc: daysAgo(90),
      printerIds: printers.map((p) => p.id),
      worstState: states.includes('Overdue')
        ? 'Overdue'
        : states.includes('DueSoon')
          ? 'DueSoon'
          : 'Ok',
    };
  });
}

// --- accounts ------------------------------------------------------------------------------------------------
// Every permission (PermissionCatalog.cs): the demo operator sees and can try everything.
const PERMISSIONS =
  'activity-log:list assemblies:create assemblies:delete assemblies:edit assemblies:view feed-units:add feed-units:configure feed-units:list feed-units:remove filaments:assign-profiles filaments:create filaments:list library:delete library:manage library:view maintenance:complete maintenance:delete maintenance:manage maintenance:view non-printed-components:create non-printed-components:delete non-printed-components:edit non-printed-components:view parts:create parts:delete parts:edit parts:list parts:read parts:replace-file parts:view print-jobs:cancel print-jobs:pin print-jobs:remote-control print-jobs:reorder print-jobs:requeue print-jobs:update-status print-jobs:view print-jobs:write-off printers:edit-connection printers:get-detail printers:list printers:mark-ready printers:register printers:remove printers:verify-connection production-runs:cancel production-runs:close production-runs:create production-runs:edit production-runs:view profiles:confirm-import profiles:edit-filament-raw-data profiles:edit-machine-raw-data profiles:get-filament profiles:get-machine profiles:get-process profiles:list-filament profiles:list-machine profiles:list-process profiles:preview-import settings:fees:read settings:fees:update settings:general:read settings:general:update settings:printer-costs:read settings:printer-costs:update settings:slicing-defaults:read settings:slicing-defaults:update spools:list spools:load spools:override-weight spools:record-usage spools:register spools:unload tags:list users:manage vendors:create vendors:list auth:self'.split(
    ' ',
  );
const whoami: WhoAmIResult = {
  ...currentUser,
  isAdministrator: true,
  groups: ['Administrators'],
  permissions: PERMISSIONS,
};
const GROUPS: GroupResponse[] = [
  {
    id: id('6a00', 1),
    name: 'Administrators',
    description: 'Every permission.',
    isProtected: true,
    grants: PERMISSIONS,
    memberCount: 1,
  },
  {
    id: id('6a00', 2),
    name: 'Operators',
    description: 'Run the farm day to day.',
    isProtected: false,
    grants: PERMISSIONS.filter(
      (p) => !p.startsWith('settings') && p !== 'users:manage',
    ),
    memberCount: 3,
  },
  {
    id: id('6a00', 3),
    name: 'Viewers',
    description: 'Read-only access.',
    isProtected: false,
    grants: PERMISSIONS.filter((p) => /:(list|view|read|get-detail)$/.test(p)),
    memberCount: 2,
  },
];
const ACCOUNTS: AccountResponse[] = [
  ['demo', 'Demo operator', 0],
  ['mira', 'Mira Chen', 1],
  ['jonas', 'Jonas Weber', 1],
  ['ade', 'Ade Okafor', 1],
  ['sam', 'Sam Rivera', 2],
].map(([username, name, group], index) => ({
  id: index === 0 ? currentUser.userId : id('0be7', index + 1),
  username: username as string,
  email: `${username}@openfarm.local`,
  name: name as string,
  isEnabled: true,
  createdAtUtc: daysAgo(100 - index * 7),
  groups: [
    { id: GROUPS[group as number].id, name: GROUPS[group as number].name },
  ],
  extraGrants: [],
  effectivePermissions: GROUPS[group as number].grants,
  isAdministrator: group === 0,
  sessionCount: index < 3 ? 1 : 0,
}));

// --- HTTP plumbing -------------------------------------------------------------------------------------------
type Context = {
  params: Record<string, string>;
  query: URLSearchParams;
  body: Record<string, unknown>;
  headers: Headers;
};
type Handler = (ctx: Context) => unknown;

const json = (data: unknown, status = 200) =>
  new Response(data === undefined ? null : JSON.stringify(data), {
    status: data === undefined ? 204 : status,
    headers: data === undefined ? {} : { 'Content-Type': 'application/json' },
  });
const problem = (status: number, detail: string) =>
  new Response(
    JSON.stringify({ title: 'Not available in the demo', status, detail }),
    {
      status,
      headers: { 'Content-Type': 'application/problem+json' },
    },
  );
export const notInDemo = () => problem(403, 'Not available in the demo.');

const page = <T>(items: T[], query: URLSearchParams, fallbackSize = 25) => {
  const size = Number(query.get('pageSize') ?? fallbackSize) || fallbackSize;
  const current = Math.max(1, Number(query.get('page') ?? 1) || 1);
  return items.slice((current - 1) * size, current * size);
};
const matchesSearch = (text: string, query: URLSearchParams) =>
  !query.get('search') ||
  text.toLowerCase().includes((query.get('search') ?? '').toLowerCase());
const fakeToken = () =>
  `demo.${btoa(JSON.stringify({ sub: currentUser.userId, exp: Math.floor(now / 1000) + 10 * 365 * 86_400 }))}.demo`;

/** The simulator's hooks for state changes the visitor starts (see simulator.ts). */
export const actions = {
  markReady: (_printer: Printer) => {},
  remote: (_job: Job, _action: 'pause' | 'resume' | 'cancel') => {},
  changed: () => {},
};

const routes: [string, RegExp, string[], Handler][] = [];
function route(method: string, path: string, handler: Handler) {
  const names: string[] = [];
  const pattern = path.replace(/\{(\w+)\}/g, (_, name: string) => {
    names.push(name);
    return '([^/]+)';
  });
  routes.push([method, new RegExp(`^/api/v1${pattern}$`), names, handler]);
}
const queueView = (query: URLSearchParams) => {
  const view = (query.get('view') ?? 'queue').toLowerCase();
  return jobs.filter((j) =>
    view === 'external'
      ? j.source === 'External'
      : j.source === 'Dispatched' &&
        (view === 'backfill' ? j.mode === 'Backfill' : j.mode === 'Finite'),
  );
};
const byStatus = (list: Job[], status: string | null) =>
  !status
    ? list
    : list.filter((j) =>
        status === 'InProgress'
          ? j.status === 'Printing' || j.status === 'Paused'
          : j.status === status,
      );

// Boot and account.
route('GET', '/auth/csrf', () => ({ token: 'demo-csrf' }));
route('POST', '/auth/refresh', () => ({
  token: fakeToken(),
  expiresAtUtc: new Date(now + 3_600_000).toISOString(),
}));
route('POST', '/auth/login', () => ({
  token: fakeToken(),
  expiresAtUtc: new Date(now + 3_600_000).toISOString(),
}));
route('GET', '/auth/whoami', () => whoami);
route('GET', '/auth/display-preferences', () => ({ currencyCode: 'USD' }));
route('GET', '/auth/sessions', () => [
  {
    id: id('5e55', 1),
    createdAtUtc: minutesAgo(90),
    lastSeenAtUtc: minutesAgo(1),
    userAgent: 'Browser',
    ipAddress: '192.168.10.5',
    isCurrent: true,
  },
]);
route('GET', '/auth/tokens', () => []);
// Installation controls are simulated locally; no credentials or network settings leave the demo.
let installationSettings = {
  publicUrl: 'https://farm.example.com' as string | null,
  mcpMode: 'SignIn',
  revision: id('a996', 1),
  publicUrlManaged: false,
  mcpModeManaged: false,
  mcpEnabledOverride: null,
};
for (const path of ['/admin/settings/server', '/admin/settings/mcp']) {
  route('GET', path, () => installationSettings);
  route('PUT', path, ({ body }) => {
    installationSettings = {
      ...installationSettings,
      ...(path.endsWith('/server')
        ? {
            publicUrl:
              typeof body.publicUrl === 'string' ? body.publicUrl : null,
          }
        : { mcpMode: String(body.mode) }),
      revision: crypto.randomUUID(),
    };
    return installationSettings;
  });
}
route(
  'GET',
  '/auth/apps',
  (): ConnectedAppsResponse => ({
    mcpUrl: installationSettings.publicUrl
      ? `${installationSettings.publicUrl}/mcp`
      : null,
    signInEnabled: installationSettings.mcpMode === 'SignIn',
    mcpEnabled: installationSettings.mcpMode !== 'Disabled',
    apps: [
      {
        id: id('a995', 1),
        clientName: 'Claude',
        clientIdSuffix: '9f2a',
        returnsTo: { kind: 'Web', label: 'claude.ai' },
        access: [
          {
            area: 'Printers',
            level: 'Read',
            extras: [],
            title: 'Can see printers, their live status and feed units.',
            control: false,
          },
          {
            area: 'Queue',
            level: 'Change',
            extras: [],
            title: 'Can see, reorder, pin and requeue jobs.',
            control: false,
          },
        ],
        createdAtUtc: minutesAgo(60 * 24 * 3),
        expiresAtUtc: new Date(now + 27 * 86_400_000).toISOString(),
        lastUsedAtUtc: minutesAgo(2),
      },
    ],
  }),
);
route('GET', '/auth/tokens/options', () => ({
  permissions: PERMISSIONS,
  maxLifetimeDays: 365,
}));
route('GET', '/admin/users', () => ACCOUNTS);
route('GET', '/admin/groups', () => GROUPS);
route('GET', '/admin/permissions', () => ({
  areas: [...new Set(PERMISSIONS.map((p) => p.split(':')[0]))].map((key) => ({
    key,
    label: key[0].toUpperCase() + key.slice(1).replaceAll('-', ' '),
    noun: key,
    administratorOnly: key === 'users',
  })),
  permissions: PERMISSIONS.map((key) => ({
    key,
    area: key.split(':')[0],
    label: key.split(':').slice(1).join(' ').replaceAll('-', ' '),
    description: '',
    isRead: /:(list|view|read|get-)/.test(key),
    isSensitive: false,
    isDestructive: /:(delete|remove|cancel)/.test(key),
    isExplicit: false,
    verb: null,
    phrase: null,
  })),
}));
route('GET', '/admin/users/{id}/sessions', () => []);
route('GET', '/admin/users/{id}/tokens', () => []);
route('GET', '/admin/users/{id}/apps', (): ConnectedAppResponse[] => []);

// Settings.
route('GET', '/settings/general', () => ({
  currencyCode: 'USD',
  electricityPricePerKwh: 0.16,
  failureAllowancePercent: 5,
  supportedCurrencies: ['USD', 'EUR', 'GBP', 'MYR', 'SGD'],
}));
const feeProfiles = [{ id: id('fee5', 1), name: 'Online shop', fees: [] }];
route('GET', '/settings/fees', () => ({ profiles: feeProfiles }));
const printerCostModels: PrinterModelCostResponse[] = (
  ['P1S', 'X1C', 'A1', 'A1MINI', 'U1'] as const
).map((modelCode) => ({
  brand: modelCode === 'U1' ? 'Snapmaker' : 'Bambu',
  modelCode,
  printerPurchasePrice: {
    P1S: 699,
    X1C: 1199,
    A1: 399,
    A1MINI: 249,
    U1: 999,
  }[modelCode],
  printerUpfrontCost: null,
  lifetimeServiceCost: 150,
  lifetimePrintingHours: 6000,
  averagePrintingWatts: modelCode === 'X1C' ? 180 : 120,
  canRemove: false,
  depreciationPerHour: 0.14,
  electricityPerHour: 0.02,
}));
route('GET', '/settings/printer-costs', () => ({
  electricityPricePerKwh: 0.16,
  models: printerCostModels,
}));
route('POST', '/assemblies/quote', ({ body }) =>
  demoQuote(body as QuoteAssemblyRequest, {
    parts,
    assemblies,
    filaments,
    summary: partSummary,
    models: printerCostModels,
    failurePercent: 5,
    profiles: feeProfiles,
  }),
);
route('GET', '/settings/slicing-defaults', () => ({
  id: id('51ce', 1),
  default: { engine: 'BambuStudio', version: '2.7.1', isExplicit: true },
  available: [
    { engine: 'BambuStudio', version: '2.7.1', platform: 'Linux' },
    { engine: 'OrcaSlicer', version: '2.3.1', platform: 'Linux' },
  ],
}));
route('GET', '/slicing/readiness', () => ({
  engine: 'BambuStudio',
  version: '2.7.1',
  isReady: true,
  printers: [],
  filaments: [],
}));
route('GET', '/ping', () => ({ status: 'ok' }));

// Printers.
route('GET', '/printers', () => ({ printers: printers.map(fleetPrinter) }));
route('GET', '/printers/catalog', () => ({
  brands: [
    {
      brand: 'Bambu',
      models: (['X1C', 'P1S', 'A1', 'A1MINI'] as const).map((m) => MODELS[m]),
    },
    { brand: 'Snapmaker', models: [MODELS.U1] },
  ],
}));
route('GET', '/printers/discovered', () => []);
route('GET', '/printers/{id}', ({ params }) => {
  const printer = printerById(params.id);
  return printer ? printerDetail(printer) : problem(404, 'Printer not found.');
});
// Stills from real printers. Each poll is a new frame, so the camera shows Live; A1 and A1 mini have none.
const CAMERA_STILLS: Partial<Record<Printer['modelCode'], string>> = {
  P1S: cameraP1s,
  U1: cameraU1,
};
const cameraBlobs = new Map<string, Promise<Blob>>();
route('GET', '/printers/{id}/camera.jpg', async ({ params, headers }) => {
  const printer = printerById(params.id);
  const still = printer && CAMERA_STILLS[printer.modelCode];
  if (!still) return new Response(null, { status: 404 });
  const etag = `"${printer.id}-${Math.floor(Date.now() / 1000)}"`;
  if (headers.get('If-None-Match') === etag)
    return new Response(null, { status: 304, headers: { ETag: etag } });
  let blob = cameraBlobs.get(still);
  if (!blob) {
    blob = fetch(still).then((response) => response.blob());
    cameraBlobs.set(still, blob);
  }
  return new Response(await blob, {
    headers: {
      'Content-Type': 'image/jpeg',
      ETag: etag,
      'X-Frame-Received-At': new Date().toISOString(),
    },
  });
});
route('GET', '/printers/{printerId}/feed-units', ({ params }) => ({
  feedUnits: feedUnits(printerById(params.printerId) ?? printers[0]),
}));
route('POST', '/printers/{id}/mark-ready', ({ params }) => {
  const printer = printerById(params.id);
  if (printer?.status !== 'AwaitingClear')
    return problem(409, 'Printer is not awaiting clear.');
  actions.markReady(printer);
  return {
    id: printer.id,
    name: printer.name,
    brand: printer.brand,
    modelCode: printer.modelCode,
    ipAddress: printer.ip,
    serialNumber: printer.serial,
    status: printer.status,
    isReachable: printer.reachable,
  };
});

// Notifications: the low-stock colours (maintenance due comes from its own endpoint).
route(
  'GET',
  '/notifications',
  (): ListNotificationsResponse => ({
    items: [],
    totalCount: 0,
    lowStock: filaments.flatMap((f) =>
      f.variants
        .filter((v) => variantStock(v.id).isLow)
        .map((v) => ({
          filamentId: f.id,
          filamentVariantId: v.id,
          vendorName: f.vendorName,
          material: f.material,
          colorName: v.colorName,
          colorHex: v.colorHex,
          onHandGrams: variantStock(v.id).onHandGrams,
          thresholdGrams: v.lowStockGrams ?? null,
          daysLeft: variantStock(v.id).daysLeft,
          thresholdDays: null,
        })),
    ),
    rfidSpools: [],
    rfidDrift: [],
  }),
);

// Queue.
route('GET', '/print-jobs', ({ query }): ListQueueJobsResponse => {
  const view = queueView(query).filter((j) => matchesSearch(j.name, query));
  const external = (query.get('view') ?? '') === 'external';
  const list = byStatus(view, query.get('status')).sort((a, b) =>
    external
      ? Date.parse(b.startedAt ?? '') - Date.parse(a.startedAt ?? '')
      : (a.position ?? 1e9) - (b.position ?? 1e9) ||
        Date.parse(b.endedAt ?? '0') - Date.parse(a.endedAt ?? '0'),
  );
  const statusCounts: Record<string, number> = {};
  for (const j of view)
    statusCounts[j.status] = (statusCounts[j.status] ?? 0) + 1;
  return {
    items: page(list, query, 50).map(jobRow),
    totalCount: list.length,
    statusCounts,
  };
});
route('GET', '/print-jobs/{id}', ({ params }) => {
  const job = jobById(params.id);
  return job ? jobResponse(job) : problem(404, 'Print job not found.');
});
route('GET', '/print-jobs/{id}/matching-diagnostics', ({ params }) => {
  const job = jobById(params.id);
  return job ? matching(job) : problem(404, 'Print job not found.');
});
for (const action of ['pause', 'resume', 'cancel'] as const)
  route('POST', `/print-jobs/{id}/${action}`, ({ params }) => {
    const job = jobById(params.id);
    if (!job) return problem(404, 'Print job not found.');
    if (action === 'cancel' && job.status === 'Queued') {
      job.status = 'Cancelled';
      job.endedAt = new Date().toISOString();
      job.position = null;
      actions.changed();
    } else actions.remote(job, action);
    return jobResponse(job);
  });
const objects = (units: number) => ({
  revisionHash: '9f2c41d7',
  plate: 1,
  dialect: 'Bambu',
  objects: [],
  units: [],
  detectedCount: units,
  countSource: 'Objects',
  skipUnavailableReason: 'Not available in the demo.',
});
route('GET', '/parts/{id}/objects', ({ params }) =>
  objects(partById(params.id)?.unitsPerPlate ?? 1),
);
route('GET', '/print-jobs/{id}/objects', ({ params }) => ({
  manifest: objects(jobById(params.id)?.units ?? 1),
  selectedUnitIds: [],
  requestedObjectIds: [],
  confirmedObjectIds: [],
  observed: false,
  artifactPath: null,
  artifactHash: null,
  artifactName: null,
  requestError: null,
  excludedAt: {},
  startRequested: false,
  canSkipObjects: false,
  skipUnavailableReason: 'Not available in the demo.',
  plannedExclusions: [],
  goodUnits: 0,
}));
route('POST', '/print-jobs/{id}/reorder', ({ params, body }) => {
  const job = jobById(params.id);
  if (!job) return problem(404, 'Print job not found.');
  const after = jobById(body.afterJobId as string | null);
  const queued = jobs
    .filter(
      (j) => j.status === 'Queued' && j.mode === job.mode && j.id !== job.id,
    )
    .sort((a, b) => (a.position ?? 0) - (b.position ?? 0));
  const ordered = after
    ? queued.flatMap((j) => (j.id === after.id ? [j, job] : [j]))
    : [job, ...queued];
  ordered.forEach((j, index) => {
    j.position = index + 1;
  });
  actions.changed();
  return { jobId: job.id, queuePosition: job.position ?? 1 };
});
route('POST', '/print-jobs/{id}/pin', ({ params, body }) => {
  const job = jobById(params.id);
  if (!job) return problem(404, 'Print job not found.');
  job.assignment = 'Pinned';
  job.pinnedPrinterId = (body.printerId as string) ?? null;
  actions.changed();
  return jobResponse(job);
});
route('POST', '/print-jobs/{id}/unpin', ({ params }) => {
  const job = jobById(params.id);
  if (!job) return problem(404, 'Print job not found.');
  job.assignment = 'Unassigned';
  job.pinnedPrinterId = null;
  actions.changed();
  return jobResponse(job);
});

// Runs.
route('GET', '/production-runs', ({ query }) => {
  const status = query.get('status');
  const searched = runs.filter((r) => matchesSearch(runLabel(r), query));
  const list = searched
    .filter(
      (r) => !status || (status === 'Open' ? runOpen(r) : r.status === status),
    )
    .sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt));
  return {
    items: page(list, query).map(runSummary),
    totalCount: list.length,
    statusCounts: {
      Open: searched.filter(runOpen).length,
      Closed: searched.filter((r) => r.status === 'Closed').length,
      Cancelled: searched.filter((r) => r.status === 'Cancelled').length,
    },
  };
});
route('GET', '/production-runs/{id}', ({ params }) => {
  const run = runById(params.id);
  return run ? runDetail(run) : problem(404, 'Run not found.');
});
route('GET', '/production-demands/backfill', (): ProductionDemandResponse[] =>
  runs.flatMap((run) =>
    run.lines
      .filter((line) => line.mode === 'Backfill')
      .map((line) => ({
        id: line.id,
        productionRunId: run.id,
        name: partById(line.targetId)?.name ?? '',
        mode: 'Backfill',
        state: 'Enabled',
        printerCap: 1,
        pinnedPrinterId: null,
        targetKind: line.targetKind,
        targetId: line.targetId,
      })),
  ),
);
route('POST', '/production-demands/preview', ({ body }) =>
  plan(
    String(body.targetKind ?? 'Part'),
    String(body.targetId ?? ''),
    Number(body.quantity ?? 1),
    String(body.quantityBasis ?? 'Plates'),
  ),
);

// Filament.
route('GET', '/vendors', () => ({ vendors }));
route('GET', '/tags', () => ({
  tags: [...new Set(parts.flatMap((p) => p.tags))].map((name, index) => ({
    id: id('7a90', index + 1),
    name,
  })),
}));
route(
  'GET',
  '/filaments',
  (): ListFilamentsResponse => ({
    filaments: filaments.map(filamentResponse),
    stock: filaments.flatMap((f) => f.variants.map((v) => variantStock(v.id))),
  }),
);
route('GET', '/filaments/shortages', () => ({ shortages: [] }));
route('GET', '/filaments/profile-assignment-fleet', () => ({
  printers: [],
  filaments: [],
}));
route('GET', '/filaments/{id}/profile-assignments', () => ({
  assignments: [],
}));
route('GET', '/spools', ({ query }) => {
  const list = spools.filter(
    (s) =>
      (!query.get('state') || s.state === query.get('state')) &&
      (!query.get('filamentId') ||
        variant(s.variantId).filament.id === query.get('filamentId')) &&
      (!query.get('filamentVariantId') ||
        s.variantId === query.get('filamentVariantId')),
  );
  return {
    spools: page(list, query, 100).map(spoolResponse),
    totalCount: list.length,
  };
});
route('GET', '/spools/{id}', ({ params }) => {
  const s = spoolById(params.id);
  return s ? spoolResponse(s) : problem(404, 'Spool not found.');
});
function history(
  filter: (s: (typeof spools)[number]) => boolean,
): SpoolHistoryEntryResponse[] {
  return jobs
    .filter((j) => j.status === 'Completed' && j.source === 'Dispatched')
    .flatMap((j, index) => {
      const p = partById(j.partId);
      const s = p
        ? spools.find((x) => x.variantId === p.variantId && filter(x))
        : undefined;
      if (!p || !s) return [];
      const { filament, variant: v } = variant(s.variantId);
      return [
        {
          id: id('4157', index + 1),
          recordedAtUtc: j.endedAt ?? daysAgo(1),
          changeGrams: -p.gramsPerPlate,
          remainingAfterGrams: Math.round(s.remaining),
          source: 'Print',
          reason: null,
          printId: j.id,
          printName: j.name,
          printStatus: j.status,
          spoolId: s.id,
          spoolLabel: null,
          material: filament.material,
          colorName: v.colorName,
          colorHex: v.colorHex,
        },
      ];
    });
}
route('GET', '/spools/history', ({ query }) => {
  const entries = history(
    (s) =>
      !query.get('filamentId') ||
      variant(s.variantId).filament.id === query.get('filamentId'),
  );
  return { entries: page(entries, query), totalCount: entries.length };
});
route('GET', '/spools/{id}/history', ({ params, query }) => {
  const entries = history((s) => s.id === params.id);
  return { entries: page(entries, query), totalCount: entries.length };
});
route('GET', '/reports/daily-usage', ({ query }) => {
  const days = Array.from({ length: 30 }, (_, i) =>
    new Date(now - (29 - i) * 86_400_000).toISOString().slice(0, 10),
  );
  const variants = filaments
    .filter((f) => !query.get('filamentId') || f.id === query.get('filamentId'))
    .flatMap((f) => f.variants);
  return {
    days,
    colors: variants.map((v, k) => ({
      filamentVariantId: v.id,
      drawnGrams: days.map((_, i) =>
        Math.max(0, Math.round(60 + 50 * Math.sin((i + k * 3) / 2.5))),
      ),
      wasteGrams: days.map((_, i) => ((i + k) % 5 === 0 ? 6 : 1)),
    })),
  };
});
route('GET', '/reports/materials', ({ query }) => {
  const rows = filaments
    .filter((f) => !query.get('filamentId') || f.id === query.get('filamentId'))
    .flatMap((f) =>
      f.variants.map((v) => {
        const used = variantStock(v.id).usedLast30DaysGrams ?? 0;
        return {
          filamentVariantId: v.id,
          vendorName: f.vendorName,
          material: f.material,
          colorName: v.colorName,
          colorHex: v.colorHex,
          usedGrams: used,
          wasteGrams: Math.round(used * 0.04),
          wasteShare: used ? 0.04 : null,
          writtenOffGrams: 0,
          cost: Math.round(used * f.pricePerKg) / 1000,
          unpricedGrams: 0,
          printCount: Math.round(used / 50),
          notDeductedCount: 0,
        };
      }),
    );
  const sum = (key: 'usedGrams' | 'wasteGrams' | 'cost' | 'printCount') =>
    rows.reduce((total, row) => total + row[key], 0);
  const total = {
    filamentVariantId: null,
    vendorName: null,
    material: null,
    colorName: null,
    colorHex: null,
    usedGrams: sum('usedGrams'),
    wasteGrams: sum('wasteGrams'),
    wasteShare: 0.04,
    writtenOffGrams: 0,
    cost: sum('cost'),
    unpricedGrams: 0,
    printCount: sum('printCount'),
    notDeductedCount: 0,
  };
  return { rows, total };
});
route('GET', '/reports/stats', statsReport);

// Library.
route('GET', '/library/{library}/contents', ({ params, query }) => {
  const contents = libraryContents(params.library, query.get('parentId'));
  return { ...contents, items: page(contents.items ?? [], query) };
});
route('GET', '/library/{library}/folders', ({ params }) => ({
  items: folders
    .filter((f) => f.library === params.library)
    .map((f) => ({
      id: f.id,
      library: f.library,
      parentId: null,
      name: f.name,
      createdAtUtc: f.createdAt,
      updatedAtUtc: f.createdAt,
    })),
  totalCount: folders.filter((f) => f.library === params.library).length,
  ancestors: [],
}));
route('GET', '/parts', ({ query }) => {
  const list = parts.filter(
    (p) => !query.get('tag') || p.tags.includes(query.get('tag') ?? ''),
  );
  return {
    parts: page(list, query, 100).map(partSummary),
    totalCount: list.length,
  };
});
route('GET', '/parts/{id}', ({ params }) => {
  const p = partById(params.id);
  return p ? partResponse(p) : problem(404, 'Part not found.');
});
route('GET', '/assemblies', ({ query }) => ({
  items: page(assemblies, query, 100).map(assemblySummary),
  totalCount: assemblies.length,
}));
route('GET', '/assemblies/{id}', ({ params }) => {
  const a = assemblyById(params.id);
  return a ? assemblyResponse(a) : problem(404, 'Assembly not found.');
});
route(
  'GET',
  '/assemblies/{id}/explosion',
  ({ params, query }): ExplodeAssemblyResponse | Response => {
    const a = assemblyById(params.id);
    if (!a) return problem(404, 'Assembly not found.');
    const quantity = Number(query.get('quantity') ?? 1) || 1;
    return {
      machineCosts: a.lines.map((line) => ({
        partId: line.partId,
        brand: 'Bambu',
        modelCode: 'P1S',
        hours:
          (((partById(line.partId)?.minutesPerPlate ?? 0) / 60) *
            line.quantity *
            quantity) /
          (partById(line.partId)?.unitsPerPlate ?? 1),
        printerDepreciation: 0.14,
        electricity: 0.02,
      })),
      productionCosts: [],
      finalProductCosts: [],
      printedParts: a.lines.map((line) => ({
        partId: line.partId,
        partName: partById(line.partId)?.name,
        quantity: line.quantity * quantity,
      })),
      nonPrintedComponents: [],
    };
  },
);
route('GET', '/non-printed-components', () => ({ items: [], totalCount: 0 }));
route('GET', '/cost-items', () => ({ items: [], totalCount: 0 }));

// Maintenance.
route('GET', '/maintenance/tasks', () => ({ items: tasks() }));
route('GET', '/maintenance/enrollments', ({ query }) => {
  const state = query.get('state');
  return {
    items: enrollments().filter(
      (e) =>
        (!state ||
          (state === 'attention' ? e.state !== 'Ok' : e.state === state)) &&
        (!query.get('printerId') || e.printerId === query.get('printerId')) &&
        (!query.get('taskId') || e.taskId === query.get('taskId')),
    ),
  };
});
route('GET', '/maintenance/logs', () => ({ items: [], totalCount: 0 }));

// Profiles and activity.
const profileList = (names: string[]) => ({
  items: names.map((name, index) => ({
    id: id('3ac0', index + 1),
    name,
    origin: 'System',
    vendor: 'BBL',
    engine: 'BambuStudio',
    sourceVersion: '2.7.1',
  })),
  totalCount: names.length,
});
route('GET', '/profiles/machine', () =>
  profileList([
    'Bambu Lab P1S 0.4 nozzle',
    'Bambu Lab X1 Carbon 0.6 nozzle',
    'Bambu Lab A1 0.4 nozzle',
    'Bambu Lab A1 mini 0.4 nozzle',
  ]),
);
route('GET', '/profiles/process', () =>
  profileList([
    '0.20mm Standard @BBL X1C',
    '0.16mm Optimal @BBL X1C',
    '0.24mm Draft @BBL X1C',
  ]),
);
route('GET', '/profiles/filament', () =>
  profileList([
    'Bambu PLA Basic @BBL X1C',
    'Bambu PLA Matte @BBL X1C',
    'Bambu PETG HF @BBL X1C',
    'Generic PLA @BBL A1',
  ]),
);
route('GET', '/activity-log', ({ query }): ActivityLogPageResponse => {
  const events = [
    ...printers.flatMap((printer, index) => [
      {
        id: id('ac71', index * 2 + 1),
        entityType: 'Printer',
        entityId: printer.id,
        eventType: 'PrinterStatusChanged',
        actorId: null,
        actorType: 'System',
        actorDisplayName: null,
        timestampUtc: minutesAgo(5 + index * 11),
        details: {
          previousState: 'IDLE',
          reportedState: printer.reportedState,
          observedAtUtc: minutesAgo(5 + index * 11),
          previousAlerts: null,
          activeAlerts: null,
        },
      },
      {
        id: id('ac71', index * 2 + 2),
        entityType: 'Printer',
        entityId: printer.id,
        eventType: 'PrinterMarkedReady',
        actorId: currentUser.userId,
        actorType: 'User',
        actorDisplayName: 'Demo operator',
        timestampUtc: minutesAgo(70 + index * 13),
        details: null,
      },
    ]),
    {
      id: id('ac72', 1),
      entityType: 'PrintJob',
      entityId: id('ac73', 1),
      eventType: 'PrintJobReordered',
      actorId: currentUser.userId,
      actorType: 'User',
      actorDisplayName: 'Demo operator',
      timestampUtc: minutesAgo(42),
      details: {
        previousState: null,
        reportedState: null,
        observedAtUtc: null,
        previousAlerts: null,
        activeAlerts: null,
        via: {
          kind: 'Token',
          credentialId: id('ac74', 1),
          label: 'Shop dashboard',
        },
      },
    },
    {
      id: id('ac72', 2),
      entityType: 'PrintJob',
      entityId: id('ac73', 2),
      eventType: 'PrintJobPinned',
      actorId: currentUser.userId,
      actorType: 'User',
      actorDisplayName: 'Demo operator',
      timestampUtc: minutesAgo(18),
      details: {
        previousState: null,
        reportedState: null,
        observedAtUtc: null,
        previousAlerts: null,
        activeAlerts: null,
        via: { kind: 'App', credentialId: id('a995', 1), label: 'Claude' },
      },
    },
  ].sort((a, b) => Date.parse(b.timestampUtc) - Date.parse(a.timestampUtc));
  const size = Number(query.get('pageSize') ?? 50) || 50;
  return {
    items: page(events, query, size),
    page: Number(query.get('page') ?? 1),
    pageSize: size,
    totalCount: events.length,
  };
});

/** Serves one /api/v1 request from the demo farm. */
export async function handle(request: Request): Promise<Response> {
  const url = new URL(request.url);
  const method = request.method.toUpperCase();
  for (const [m, pattern, names, handler] of routes) {
    if (m !== method) continue;
    const match = url.pathname.match(pattern);
    if (!match) continue;
    let body: Record<string, unknown> = {};
    if (method !== 'GET') {
      // Visible "pending" states (e.g. "Marking ready…") need a beat.
      await new Promise((resolve) => setTimeout(resolve, 350));
      try {
        body = (await request.clone().json()) as Record<string, unknown>;
      } catch {
        body = {};
      }
    }
    const params = Object.fromEntries(
      names.map((name, index) => [name, decodeURIComponent(match[index + 1])]),
    );
    const result = await handler({
      params,
      query: url.searchParams,
      body,
      headers: request.headers,
    });
    return result instanceof Response ? result : json(result);
  }
  if (method === 'GET') {
    if (import.meta.env.DEV)
      console.warn(`[demo] unhandled GET ${url.pathname}${url.search}`);
    return problem(404, 'Not in the demo.');
  }
  return notInDemo();
}
