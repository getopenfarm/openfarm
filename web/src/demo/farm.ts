// The marketing-site demo farm: synthetic records only (no customer data). api.ts turns this state into the
// API's real response types, so a contract change fails the demo build where it needs updating.

import basePng from './assets/base.png?inline';
import bracketPng from './assets/bracket.png?inline';
import clipPng from './assets/clip.png?inline';
import coasterPng from './assets/coaster.png?inline';
import cubePng from './assets/cube.png?inline';
import hookPng from './assets/hook.png?inline';
import knobPng from './assets/knob.png?inline';
import lidPng from './assets/lid.png?inline';
import potPng from './assets/pot.png?inline';
import standPng from './assets/stand.png?inline';

const base64 = (dataUrl: string) => dataUrl.slice(dataUrl.indexOf(',') + 1);
export const id = (prefix: string, n: number) =>
  `${prefix.padEnd(8, '0')}-0000-4000-8000-${n.toString(16).padStart(12, '0')}`;

export const now = Date.now();
export const minutesAgo = (minutes: number) =>
  new Date(now - minutes * 60_000).toISOString();
export const daysAgo = (days: number) => minutesAgo(days * 24 * 60);
export const dayOffset = (days: number) =>
  new Date(now + days * 86_400_000).toISOString().slice(0, 10);

export type JobStatus =
  | 'Queued'
  | 'Printing'
  | 'Paused'
  | 'Completed'
  | 'Failed'
  | 'Cancelled';

export interface Variant {
  id: string;
  colorName: string;
  colorHex: string;
  lowStockGrams?: number;
}
export interface Filament {
  id: string;
  vendorId: string;
  vendorName: string;
  material: string;
  materialType: string;
  pricePerKg: number;
  variants: Variant[];
}
export interface Spool {
  id: string;
  variantId: string;
  remaining: number;
  weight: number;
  state: 'InStorage' | 'Using' | 'Finished';
  slotId: string | null;
  price: number | null;
  receivedOn: string;
}
export interface FeedSlot {
  id: string;
  position: number;
  label: string;
  spoolId: string | null;
  active: boolean;
}
export interface FeedUnit {
  id: string;
  kind: 'MultiMaterialUnit' | 'ExternalSpool';
  unitIndex: number;
  label: string;
  slots: FeedSlot[];
}
export interface Printer {
  id: string;
  name: string;
  brand: 'Bambu' | 'Snapmaker';
  modelCode: 'P1S' | 'X1C' | 'A1' | 'A1MINI' | 'U1';
  ip: string;
  serial: string;
  status: 'Idle' | 'Printing' | 'AwaitingClear';
  reachable: boolean;
  nozzle: number;
  reportedState: string;
  jobId: string | null;
  progress: number;
  remainingMinutes: number;
  nozzleTemp: number;
  bedTemp: number;
  feed: FeedUnit[];
  createdAt: string;
}
export interface Part {
  id: string;
  name: string;
  thumbnail: string | null;
  variantId: string;
  gramsPerPlate: number;
  minutesPerPlate: number;
  unitsPerPlate: number;
  nozzle: number;
  tags: string[];
  folderId: string | null;
  createdAt: string;
}
export interface Assembly {
  id: string;
  name: string;
  description: string;
  lines: { partId: string; quantity: number }[];
  folderId: string | null;
  createdAt: string;
}
export interface Requirement {
  id: string;
  partId: string;
  /** Units to make; null = unlimited (backfill). */
  quantity: number | null;
}
export interface RunLine {
  id: string;
  targetKind: 'Part' | 'Assembly';
  targetId: string;
  quantity: number | null;
  quantityBasis: 'Units' | 'Sets' | 'Plates';
  mode: 'Finite' | 'Backfill';
  requirements: Requirement[];
}
export interface Run {
  id: string;
  name: string | null;
  status: 'Active' | 'Complete' | 'Closed' | 'Cancelled';
  dueDate: string | null;
  notes: string | null;
  createdAt: string;
  lines: RunLine[];
}
export interface Job {
  id: string;
  partId: string;
  requirementId: string | null;
  runId: string | null;
  lineId: string | null;
  mode: 'Finite' | 'Backfill';
  status: JobStatus;
  units: number;
  position: number | null;
  assignment: 'Unassigned' | 'Pinned';
  pinnedPrinterId: string | null;
  printerId: string | null;
  source: 'Dispatched' | 'External';
  name: string;
  createdAt: string;
  startedAt: string | null;
  endedAt: string | null;
  lastBlockReason: string | null;
}

export const vendors = [
  { id: id('7e4d0', 1), name: 'Bambu Lab' },
  { id: id('7e4d0', 2), name: 'Polymaker' },
];

export const filaments: Filament[] = [
  {
    id: id('f1a0', 1),
    vendorId: vendors[0].id,
    vendorName: 'Bambu Lab',
    material: 'PLA Basic',
    materialType: 'PLA',
    pricePerKg: 19.99,
    variants: [
      { id: id('f1a1', 1), colorName: 'Black', colorHex: '#000000' },
      {
        id: id('f1a1', 2),
        colorName: 'Jade White',
        colorHex: '#FFFFFF',
        lowStockGrams: 3000,
      },
      { id: id('f1a1', 3), colorName: 'Blue', colorHex: '#0A2989' },
      { id: id('f1a1', 4), colorName: 'Red', colorHex: '#C12E1F' },
      { id: id('f1a1', 5), colorName: 'Orange', colorHex: '#FF6A13' },
      { id: id('f1a1', 6), colorName: 'Bambu Green', colorHex: '#00AE42' },
    ],
  },
  {
    id: id('f1a0', 2),
    vendorId: vendors[0].id,
    vendorName: 'Bambu Lab',
    material: 'PLA Matte',
    materialType: 'PLA',
    pricePerKg: 24.99,
    variants: [
      { id: id('f1a2', 1), colorName: 'Charcoal', colorHex: '#000000' },
      { id: id('f1a2', 2), colorName: 'Ivory White', colorHex: '#FFFFFF' },
      { id: id('f1a2', 3), colorName: 'Ash Gray', colorHex: '#9B9EA0' },
      { id: id('f1a2', 4), colorName: 'Sakura Pink', colorHex: '#E8AFCF' },
    ],
  },
  {
    id: id('f1a0', 3),
    vendorId: vendors[0].id,
    vendorName: 'Bambu Lab',
    material: 'PETG HF',
    materialType: 'PETG',
    pricePerKg: 21.99,
    variants: [
      { id: id('f1a3', 1), colorName: 'Orange', colorHex: '#F75403' },
      { id: id('f1a3', 2), colorName: 'Black', colorHex: '#000000' },
      { id: id('f1a3', 3), colorName: 'Gray', colorHex: '#ADB1B2' },
    ],
  },
  {
    id: id('f1a0', 4),
    vendorId: vendors[1].id,
    vendorName: 'Polymaker',
    material: 'PolyTerra PLA',
    materialType: 'PLA',
    pricePerKg: 17.49,
    variants: [
      { id: id('f1a4', 1), colorName: 'Arctic Teal', colorHex: '#2EA6A6' },
      { id: id('f1a4', 2), colorName: 'Savannah Yellow', colorHex: '#F2C230' },
      { id: id('f1a4', 3), colorName: 'Forest Green', colorHex: '#3B6E3B' },
      { id: id('f1a4', 4), colorName: 'Muted White', colorHex: '#F2EFE6' },
    ],
  },
];
export const variant = (variantId: string) => {
  for (const filament of filaments)
    for (const v of filament.variants)
      if (v.id === variantId) return { filament, variant: v };
  throw new Error(`Unknown demo variant ${variantId}`);
};
const V = {
  black: id('f1a1', 1),
  white: id('f1a1', 2),
  blue: id('f1a1', 3),
  red: id('f1a1', 4),
  orange: id('f1a1', 5),
  green: id('f1a1', 6),
  matteBlack: id('f1a2', 1),
  matteGray: id('f1a2', 3),
  pink: id('f1a2', 4),
  petgOrange: id('f1a3', 1),
  petgBlack: id('f1a3', 2),
  teal: id('f1a4', 1),
  yellow: id('f1a4', 2),
  forest: id('f1a4', 3),
};

let spoolSeq = 0;
const spool = (
  variantId: string,
  remaining: number,
  state: Spool['state'] = 'InStorage',
  slotId: string | null = null,
): Spool => ({
  id: id('5900', ++spoolSeq),
  variantId,
  remaining,
  weight: 1000,
  state,
  slotId,
  price: variant(variantId).filament.pricePerKg,
  receivedOn: dayOffset(-20 - spoolSeq),
});

let slotSeq = 0;
let unitSeq = 0;
/** One AMS / AMS lite / toolhead set; each listed spool is loaded in the next slot. */
function unit(
  kind: FeedUnit['kind'],
  label: string,
  loads: (readonly [string, number] | null)[],
  activeIndex: number,
  spools: Spool[],
): FeedUnit {
  const slots = loads.map((load, index): FeedSlot => {
    const slotId = id('51e7', ++slotSeq);
    let spoolId: string | null = null;
    if (load) {
      const loaded = spool(load[0], load[1], 'Using', slotId);
      spools.push(loaded);
      spoolId = loaded.id;
    }
    return {
      id: slotId,
      position: index,
      label:
        kind === 'ExternalSpool'
          ? 'External'
          : label.startsWith('Tool')
            ? `T${index + 1}`
            : `A${index + 1}`,
      spoolId,
      active: index === activeIndex,
    };
  });
  return { id: id('fee0', ++unitSeq), kind, unitIndex: 0, label, slots };
}

export const spools: Spool[] = [];
const ams = (loads: (readonly [string, number] | null)[], active: number) => [
  unit('MultiMaterialUnit', 'AMS 1', loads, active, spools),
];

const printerSeed: Omit<Printer, 'id' | 'createdAt'>[] = [
  {
    name: 'A1-01',
    brand: 'Bambu',
    modelCode: 'A1',
    ip: '192.168.10.31',
    serial: '03919D000000031',
    status: 'Printing',
    reachable: true,
    nozzle: 0.4,
    reportedState: 'RUNNING',
    jobId: null,
    progress: 64,
    remainingMinutes: 47,
    nozzleTemp: 220,
    bedTemp: 65,
    feed: [
      unit(
        'MultiMaterialUnit',
        'AMS lite',
        [
          [V.teal, 640],
          [V.yellow, 905],
          [V.white, 210],
          [V.forest, 780],
        ],
        0,
        spools,
      ),
    ],
  },
  {
    name: 'A1M-01',
    brand: 'Bambu',
    modelCode: 'A1MINI',
    ip: '192.168.10.32',
    serial: '0300AA000000032',
    status: 'Idle',
    reachable: true,
    nozzle: 0.4,
    reportedState: 'IDLE',
    jobId: null,
    progress: 0,
    remainingMinutes: 0,
    nozzleTemp: 27,
    bedTemp: 26,
    feed: [
      unit('ExternalSpool', 'External spool', [[V.petgOrange, 870]], 0, spools),
    ],
  },
  {
    name: 'P1S-01',
    brand: 'Bambu',
    modelCode: 'P1S',
    ip: '192.168.10.21',
    serial: '01P00C000000021',
    status: 'Printing',
    reachable: true,
    nozzle: 0.4,
    reportedState: 'RUNNING',
    jobId: null,
    progress: 38,
    remainingMinutes: 116,
    nozzleTemp: 220,
    bedTemp: 55,
    feed: ams(
      [
        [V.black, 742],
        [V.white, 318],
        [V.red, 1000],
        [V.blue, 455],
      ],
      0,
    ),
  },
  {
    name: 'P1S-02',
    brand: 'Bambu',
    modelCode: 'P1S',
    ip: '192.168.10.22',
    serial: '01P00C000000022',
    status: 'Printing',
    reachable: true,
    nozzle: 0.4,
    reportedState: 'RUNNING',
    jobId: null,
    progress: 91,
    remainingMinutes: 9,
    nozzleTemp: 220,
    bedTemp: 55,
    feed: ams([[V.matteBlack, 512], [V.matteGray, 880], null, [V.pink, 96]], 0),
  },
  {
    name: 'P1S-03',
    brand: 'Bambu',
    modelCode: 'P1S',
    ip: '192.168.10.23',
    serial: '01P00C000000023',
    status: 'AwaitingClear',
    reachable: true,
    nozzle: 0.4,
    reportedState: 'FINISH',
    jobId: null,
    progress: 100,
    remainingMinutes: 0,
    nozzleTemp: 38,
    bedTemp: 34,
    feed: ams(
      [
        [V.black, 488],
        [V.orange, 731],
        [V.green, 1000],
        [V.white, 655],
      ],
      0,
    ),
  },
  {
    name: 'P1S-04',
    brand: 'Bambu',
    modelCode: 'P1S',
    ip: '192.168.10.24',
    serial: '01P00C000000024',
    status: 'Printing',
    reachable: true,
    nozzle: 0.4,
    reportedState: 'RUNNING',
    jobId: null,
    progress: 17,
    remainingMinutes: 212,
    nozzleTemp: 220,
    bedTemp: 55,
    feed: ams([[V.matteBlack, 930], [V.black, 166], [V.blue, 820], null], 0),
  },
  {
    name: 'U1-01',
    brand: 'Snapmaker',
    modelCode: 'U1',
    ip: '192.168.10.41',
    serial: 'SM-U1-0041',
    status: 'Printing',
    reachable: true,
    nozzle: 0.4,
    reportedState: 'RUNNING',
    jobId: null,
    progress: 52,
    remainingMinutes: 74,
    nozzleTemp: 215,
    bedTemp: 60,
    feed: [
      unit(
        'MultiMaterialUnit',
        'Toolheads',
        [
          [V.teal, 702],
          [V.yellow, 615],
          [V.white, 540],
          [V.forest, 388],
        ],
        0,
        spools,
      ),
    ],
  },
  {
    name: 'X1C-01',
    brand: 'Bambu',
    modelCode: 'X1C',
    ip: '192.168.10.11',
    serial: '00M00A000000011',
    status: 'Idle',
    reachable: true,
    nozzle: 0.6,
    reportedState: 'IDLE',
    jobId: null,
    progress: 0,
    remainingMinutes: 0,
    nozzleTemp: 28,
    bedTemp: 27,
    feed: ams(
      [
        [V.petgBlack, 860],
        [V.petgOrange, 404],
        [V.black, 1000],
        [V.matteGray, 233],
      ],
      2,
    ),
  },
];
export const printers: Printer[] = printerSeed.map((printer, index) => ({
  ...printer,
  id: id('9a1e', index + 1),
  createdAt: daysAgo(120 - index * 9),
}));
const P = Object.fromEntries(
  printers.map((printer) => [printer.name, printer]),
);

// Shelf stock beyond the loaded spools.
spools.push(
  spool(V.black, 1000),
  spool(V.black, 1000),
  spool(V.black, 1000),
  spool(V.white, 1000),
  spool(V.blue, 1000),
  spool(V.red, 620),
  spool(V.orange, 1000),
  spool(V.matteBlack, 1000),
  spool(V.matteBlack, 1000),
  spool(V.teal, 1000),
  spool(V.petgOrange, 1000),
  spool(V.yellow, 1000),
  { ...spool(V.white, 0, 'Finished'), remaining: 0 },
  { ...spool(V.black, 0, 'Finished'), remaining: 0 },
);

export const folders = [
  {
    id: id('f01d', 1),
    library: 'parts',
    name: 'Desk accessories',
    createdAt: daysAgo(60),
  },
  {
    id: id('f01d', 2),
    library: 'parts',
    name: 'Enclosure',
    createdAt: daysAgo(40),
  },
];
const [deskFolder, enclosureFolder] = folders;

const part = (
  n: number,
  name: string,
  png: string,
  variantId: string,
  gramsPerPlate: number,
  minutesPerPlate: number,
  unitsPerPlate: number,
  tags: string[],
  folderId: string | null = null,
): Part => ({
  id: id('9a27', n),
  name,
  thumbnail: base64(png),
  variantId,
  gramsPerPlate,
  minutesPerPlate,
  unitsPerPlate,
  nozzle: 0.4,
  tags,
  folderId,
  createdAt: daysAgo(70 - n * 4),
});
export const parts: Part[] = [
  part(1, 'Cable clip', clipPng, V.black, 46, 72, 4, ['desk'], deskFolder.id),
  part(
    2,
    'Enclosure base',
    basePng,
    V.matteBlack,
    118,
    236,
    4,
    ['enclosure'],
    enclosureFolder.id,
  ),
  part(
    3,
    'Enclosure lid',
    lidPng,
    V.matteBlack,
    74,
    152,
    4,
    ['enclosure'],
    enclosureFolder.id,
  ),
  part(4, 'Phone stand', standPng, V.teal, 61, 98, 2, ['desk'], deskFolder.id),
  part(5, 'Desk hook', hookPng, V.orange, 38, 64, 4, ['desk'], deskFolder.id),
  part(6, 'Plant pot', potPng, V.white, 86, 134, 1, ['home']),
  part(7, 'Coaster', coasterPng, V.forest, 52, 88, 4, ['home']),
  part(
    8,
    'Control knob',
    knobPng,
    V.black,
    12,
    26,
    8,
    ['enclosure'],
    enclosureFolder.id,
  ),
  part(9, 'Mounting bracket', bracketPng, V.petgBlack, 34, 58, 4, ['hardware']),
  part(10, 'Calibration cube', cubePng, V.black, 8, 18, 1, ['test']),
];
const T = Object.fromEntries(parts.map((p) => [p.name, p]));

export const assemblies: Assembly[] = [
  {
    id: id('a55e', 1),
    name: 'Electronics enclosure',
    description: 'Base, lid and two knobs.',
    lines: [
      { partId: T['Enclosure base'].id, quantity: 1 },
      { partId: T['Enclosure lid'].id, quantity: 1 },
      { partId: T['Control knob'].id, quantity: 2 },
    ],
    folderId: null,
    createdAt: daysAgo(38),
  },
  {
    id: id('a55e', 2),
    name: 'Desk set',
    description: 'Stand, hook and four cable clips.',
    lines: [
      { partId: T['Phone stand'].id, quantity: 1 },
      { partId: T['Desk hook'].id, quantity: 1 },
      { partId: T['Cable clip'].id, quantity: 4 },
    ],
    folderId: null,
    createdAt: daysAgo(30),
  },
];

let requirementSeq = 0;
const requirement = (partId: string, quantity: number | null): Requirement => ({
  id: id('ae90', ++requirementSeq),
  partId,
  quantity,
});
export const runs: Run[] = [
  {
    id: id('a0e1', 1),
    name: null,
    status: 'Active',
    dueDate: dayOffset(3),
    notes: null,
    createdAt: daysAgo(2),
    lines: [
      {
        id: id('11e0', 1),
        targetKind: 'Assembly',
        targetId: assemblies[0].id,
        quantity: 8,
        quantityBasis: 'Sets',
        mode: 'Finite',
        requirements: [
          requirement(T['Enclosure base'].id, 8),
          requirement(T['Enclosure lid'].id, 8),
          requirement(T['Control knob'].id, 16),
        ],
      },
    ],
  },
  {
    id: id('a0e1', 2),
    name: 'Workshop order 0417',
    status: 'Active',
    dueDate: dayOffset(1),
    notes: 'Pack clips in bags of 12.',
    createdAt: daysAgo(1),
    lines: [
      {
        id: id('11e0', 2),
        targetKind: 'Part',
        targetId: T['Cable clip'].id,
        quantity: 24,
        quantityBasis: 'Units',
        mode: 'Finite',
        requirements: [requirement(T['Cable clip'].id, 24)],
      },
      {
        id: id('11e0', 3),
        targetKind: 'Part',
        targetId: T['Desk hook'].id,
        quantity: 8,
        quantityBasis: 'Units',
        mode: 'Finite',
        requirements: [requirement(T['Desk hook'].id, 8)],
      },
    ],
  },
  {
    id: id('a0e1', 3),
    name: null,
    status: 'Active',
    dueDate: dayOffset(-1),
    notes: null,
    createdAt: daysAgo(4),
    lines: [
      {
        id: id('11e0', 4),
        targetKind: 'Part',
        targetId: T.Coaster.id,
        quantity: 12,
        quantityBasis: 'Units',
        mode: 'Finite',
        requirements: [requirement(T.Coaster.id, 12)],
      },
    ],
  },
  {
    id: id('a0e1', 4),
    name: null,
    status: 'Active',
    dueDate: null,
    notes: null,
    createdAt: daysAgo(9),
    lines: [
      {
        id: id('11e0', 5),
        targetKind: 'Part',
        targetId: T['Phone stand'].id,
        quantity: null,
        quantityBasis: 'Units',
        mode: 'Backfill',
        requirements: [requirement(T['Phone stand'].id, null)],
      },
    ],
  },
  {
    id: id('a0e1', 5),
    name: 'Market stall restock',
    status: 'Closed',
    dueDate: dayOffset(-6),
    notes: null,
    createdAt: daysAgo(12),
    lines: [
      {
        id: id('11e0', 6),
        targetKind: 'Part',
        targetId: T['Plant pot'].id,
        quantity: 6,
        quantityBasis: 'Units',
        mode: 'Finite',
        requirements: [requirement(T['Plant pot'].id, 6)],
      },
    ],
  },
];
const req = (runIndex: number, lineIndex = 0, reqIndex = 0) => {
  const run = runs[runIndex];
  const line = run.lines[lineIndex];
  return { run, line, requirement: line.requirements[reqIndex] };
};

let jobSeq = 0;
let positionSeq = 0;
function job(
  target: ReturnType<typeof req>,
  status: JobStatus,
  options: Partial<Job> = {},
): Job {
  const partInfo = parts.find((p) => p.id === target.requirement.partId);
  if (!partInfo) throw new Error('Unknown demo part');
  const queued = status === 'Queued';
  return {
    id: id('10b0', ++jobSeq),
    partId: partInfo.id,
    requirementId: target.requirement.id,
    runId: target.run.id,
    lineId: target.line.id,
    mode: target.line.mode,
    status,
    units: partInfo.unitsPerPlate,
    position: queued ? ++positionSeq : null,
    assignment: 'Unassigned',
    pinnedPrinterId: null,
    printerId: null,
    source: 'Dispatched',
    name: partInfo.name,
    createdAt: target.run.createdAt,
    startedAt: null,
    endedAt: null,
    lastBlockReason: null,
    ...options,
  };
}
const started = (printer: Printer, minutes: number): Partial<Job> => ({
  printerId: printer.id,
  startedAt: minutesAgo(minutes),
});
const ended = (
  printer: Printer,
  minutesAgoEnd: number,
  duration: number,
): Partial<Job> => ({
  printerId: printer.id,
  startedAt: minutesAgo(minutesAgoEnd + duration),
  endedAt: minutesAgo(minutesAgoEnd),
});

export const jobs: Job[] = [
  // Electronics enclosure ×8: bases on P1S-04, lids on P1S-02, knobs waiting.
  job(req(0, 0, 0), 'Completed', ended(P['P1S-04'], 260, 236)),
  job(req(0, 0, 0), 'Printing', started(P['P1S-04'], 44)),
  job(req(0, 0, 1), 'Printing', started(P['P1S-02'], 140)),
  job(req(0, 0, 1), 'Queued'),
  job(req(0, 0, 2), 'Queued'),
  job(req(0, 0, 2), 'Queued'),
  // Workshop order: clips on P1S-01, the finished hooks still on P1S-03's bed.
  job(req(1, 0), 'Completed', ended(P['P1S-01'], 190, 72)),
  job(req(1, 0), 'Printing', started(P['P1S-01'], 45)),
  ...Array.from({ length: 4 }, () => job(req(1, 0), 'Queued')),
  job(req(1, 1), 'Completed', ended(P['P1S-03'], 6, 64)),
  job(req(1, 1), 'Queued'),
  // Coasters on the U1; one plate failed and its replacement waits for the U1.
  job(req(2), 'Failed', { ...ended(P['U1-01'], 420, 51) }),
  job(req(2), 'Completed', ended(P['U1-01'], 300, 88)),
  job(req(2), 'Printing', started(P['U1-01'], 46)),
  job(req(2), 'Queued'),
  // Backfill: phone stands on spare capacity.
  job(req(3), 'Printing', started(P['A1-01'], 52)),
  job(req(3), 'Completed', ended(P['A1-01'], 160, 98)),
  job(req(3), 'Queued'),
  // A closed run, fully made.
  ...Array.from({ length: 6 }, (_, index) =>
    job(
      req(4),
      'Completed',
      ended(P[index % 2 ? 'P1S-02' : 'P1S-03'], 4000 + index * 180, 134),
    ),
  ),
  job(req(1, 0), 'Cancelled', ended(P['P1S-01'], 1500, 12)),
];
// Printer-started prints (not from OpenFarm).
jobs.push({
  ...job(req(4), 'Completed', ended(P['X1C-01'], 900, 18)),
  partId: T['Calibration cube'].id,
  requirementId: null,
  runId: null,
  lineId: null,
  source: 'External',
  name: 'Calibration cube',
  units: 1,
});
// The finished hooks are still on P1S-03's bed.
P['P1S-03'].jobId =
  jobs.find((j) => j.name === 'Desk hook' && j.status === 'Completed')?.id ??
  null;
for (const j of jobs)
  if (j.status === 'Printing' && j.printerId)
    for (const printer of printers)
      if (printer.id === j.printerId) printer.jobId = j.id;
// The U1's coaster run is pinned to it.
for (const j of jobs)
  if (j.runId === runs[2].id && j.status === 'Queued') {
    j.assignment = 'Pinned';
    j.pinnedPrinterId = P['U1-01'].id;
  }

export const currentUser = {
  userId: id('0be7', 1),
  username: 'demo',
  email: 'demo@openfarm.local',
  displayName: 'Demo operator',
};
