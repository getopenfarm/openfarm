import {
  AngleIcon,
  BarcodeIcon,
  BoltIcon,
  BoxesIcon,
  BoxIcon,
  BriefcaseBusinessIcon,
  BubblesIcon,
  CableIcon,
  CircleIcon,
  CircleUserRoundIcon,
  CircuitBoardIcon,
  CogIcon,
  CoinsIcon,
  ConeIcon,
  ContactRoundIcon,
  CuboidIcon,
  CylinderIcon,
  DiameterIcon,
  DiamondIcon,
  DiscIcon,
  DropletIcon,
  EllipseIcon,
  FactoryIcon,
  FanIcon,
  FlameIcon,
  FlaskConicalIcon,
  ForkliftIcon,
  GaugeIcon,
  GiftIcon,
  GraduationCapIcon,
  HammerIcon,
  HandHelpingIcon,
  HandIcon,
  HandshakeIcon,
  HardHatIcon,
  HeadsetIcon,
  HeartIcon,
  HexagonIcon,
  IdCardIcon,
  KeyIcon,
  LayersIcon,
  LightbulbIcon,
  ListChecksIcon,
  type LucideIcon,
  MagnetIcon,
  MailboxIcon,
  MailIcon,
  MapPinIcon,
  NutIcon,
  OctagonIcon,
  PackageCheckIcon,
  PackageIcon,
  PaintBucketIcon,
  PaintbrushIcon,
  PaperBagIcon,
  PencilRulerIcon,
  PentagonIcon,
  PersonStandingIcon,
  PipetteIcon,
  PlaneIcon,
  Printer3dIcon,
  PuzzleIcon,
  PyramidIcon,
  RadiusIcon,
  RectangleHorizontalIcon,
  RectangleVerticalIcon,
  RecycleIcon,
  ScrewIcon,
  ScrollIcon,
  ShapesIcon,
  ShieldCheckIcon,
  ShipCargoIcon,
  SmileIcon,
  SoapDispenserDropletIcon,
  SparklesIcon,
  SplineIcon,
  SpoolIcon,
  SprayCanIcon,
  SquareIcon,
  StarIcon,
  StickerIcon,
  StoreIcon,
  ThermometerIcon,
  TimerIcon,
  TorusIcon,
  TriangleIcon,
  TriangleRightIcon,
  TruckIcon,
  TubeLotionIcon,
  UserCheckIcon,
  UserCogIcon,
  UserGroupIcon,
  UserIcon,
  UserPenIcon,
  UsersIcon,
  WarehouseIcon,
  WrenchIcon,
} from 'lucide-react';
import type { categories } from './costing';

type Category = (typeof categories)[number];

export interface CostIconOption {
  /** Lucide name, stored on the item. */
  name: string;
  label: string;
  icon: LucideIcon;
  /** Operator words for search: "glue", "ipa", "m3". */
  keywords: string;
}

type Entry = [name: string, icon: LucideIcon, label: string, keywords?: string];

// Shown as one flat grid, in this order. The first entry of each category block
// is that category's default (categoryIcon).
const entries: Entry[] = [
  // Hardware
  ['screw', ScrewIcon, 'Screw', 'fastener thread m3'],
  ['nut', NutIcon, 'Nut', 'fastener insert heat-set'],
  ['bolt', BoltIcon, 'Bolt', 'fastener rod'],
  ['magnet', MagnetIcon, 'Magnet', 'disc disk neodymium'],
  ['disc', DiscIcon, 'Disc', 'disk magnet round neodymium'],
  ['cog', CogIcon, 'Gear', 'bearing spring mechanism'],
  ['wrench', WrenchIcon, 'Tool', 'hex key'],
  ['key', KeyIcon, 'Key', 'keyring keychain'],
  ['cable', CableIcon, 'Cable', 'wire cord'],
  ['lightbulb', LightbulbIcon, 'LED', 'lamp light'],
  ['circuit-board', CircuitBoardIcon, 'PCB', 'electronics'],
  // Packaging
  ['package', PackageIcon, 'Box', 'carton parcel'],
  ['boxes', BoxesIcon, 'Cartons', 'bulk stack'],
  ['paper-bag', PaperBagIcon, 'Bag', 'pouch'],
  ['mail', MailIcon, 'Mailer', 'courier bag envelope'],
  ['sticker', StickerIcon, 'Label', 'tape decal'],
  ['barcode', BarcodeIcon, 'Barcode', 'sku'],
  ['bubbles', BubblesIcon, 'Bubble wrap', 'roll foam cushion'],
  ['scroll', ScrollIcon, 'Roll', 'sheet paper film foam wrap'],
  ['gift', GiftIcon, 'Gift', 'ribbon'],
  // Consumables
  ['droplet', DropletIcon, 'Liquid', 'oil lube'],
  ['tube-lotion', TubeLotionIcon, 'Tube', 'glue adhesive silicone grease'],
  ['pipette', PipetteIcon, 'Dispenser', 'glue dropper'],
  ['spray-can', SprayCanIcon, 'Spray', 'primer paint air'],
  ['paint-bucket', PaintBucketIcon, 'Paint'],
  ['paintbrush', PaintbrushIcon, 'Brush'],
  [
    'soap-dispenser-droplet',
    SoapDispenserDropletIcon,
    'Cleaner',
    'ipa alcohol',
  ],
  ['flask-conical', FlaskConicalIcon, 'Chemical', 'resin solvent'],
  // Labor
  ['hard-hat', HardHatIcon, 'Worker', 'assembly labor labour'],
  ['user', UserIcon, 'Operator', 'person human people staff'],
  ['users', UsersIcon, 'Team', 'people humans group crew staff'],
  ['person-standing', PersonStandingIcon, 'Person', 'human people body figure'],
  ['user-group', UserGroupIcon, 'Group', 'people humans crew staff'],
  [
    'circle-user-round',
    CircleUserRoundIcon,
    'Avatar',
    'person human people profile account customer',
  ],
  [
    'contact-round',
    ContactRoundIcon,
    'Contact',
    'person people customer supplier address card',
  ],
  [
    'user-check',
    UserCheckIcon,
    'Inspector',
    'person human people qc approved verified',
  ],
  [
    'user-cog',
    UserCogIcon,
    'Technician',
    'person human people admin maintenance repair',
  ],
  [
    'user-pen',
    UserPenIcon,
    'Designer',
    'person human people editor cad author',
  ],
  ['id-card', IdCardIcon, 'Staff ID', 'badge employee person people'],
  [
    'handshake',
    HandshakeIcon,
    'Vendor',
    'supplier partner contractor deal agreement',
  ],
  ['hand-helping', HandHelpingIcon, 'Helper', 'assist help service hand'],
  ['headset', HeadsetIcon, 'Support', 'customer service call helpdesk'],
  [
    'graduation-cap',
    GraduationCapIcon,
    'Training',
    'learn course student person',
  ],
  [
    'briefcase-business',
    BriefcaseBusinessIcon,
    'Manager',
    'business office admin contractor freelance person',
  ],
  ['smile', SmileIcon, 'Face', 'human person customer happy people'],
  ['hand', HandIcon, 'Manual', 'gloves handling'],
  ['hammer', HammerIcon, 'Build'],
  ['timer', TimerIcon, 'Time', 'hour minutes'],
  ['list-checks', ListChecksIcon, 'QC', 'inspection checklist'],
  ['pencil-ruler', PencilRulerIcon, 'Design', 'cad'],
  // Shipping
  ['truck', TruckIcon, 'Courier', 'delivery freight'],
  ['plane', PlaneIcon, 'Air', 'flight freight'],
  ['ship-cargo', ShipCargoIcon, 'Sea', 'container freight'],
  ['warehouse', WarehouseIcon, 'Warehouse', 'storage fulfilment'],
  ['mailbox', MailboxIcon, 'Postage', 'post'],
  ['map-pin', MapPinIcon, 'Pickup', 'local address'],
  ['package-check', PackageCheckIcon, 'Handling', 'pick pack'],
  ['forklift', ForkliftIcon, 'Pallet', 'freight'],
  // Printing
  ['printer-3d', Printer3dIcon, 'Printer', 'machine'],
  ['spool', SpoolIcon, 'Spool', 'filament thread wire reel'],
  ['cuboid', CuboidIcon, 'Block', 'part model'],
  ['layers', LayersIcon, 'Layers', 'build plate sheet'],
  ['flame', FlameIcon, 'Heat', 'hotend nozzle heater'],
  ['thermometer', ThermometerIcon, 'Thermistor', 'temperature sensor'],
  ['fan', FanIcon, 'Fan', 'cooling blower'],
  ['gauge', GaugeIcon, 'Gauge', 'calibration'],
  // Other
  ['shapes', ShapesIcon, 'Other', 'misc general'],
  ['puzzle', PuzzleIcon, 'Kit', 'set'],
  ['factory', FactoryIcon, 'Overhead', 'rent utilities'],
  ['sparkles', SparklesIcon, 'Finish', 'polish post-processing'],
  ['recycle', RecycleIcon, 'Waste', 'scrap'],
  ['coins', CoinsIcon, 'Fee', 'commission'],
  ['store', StoreIcon, 'Marketplace', 'platform fee'],
  ['shield-check', ShieldCheckIcon, 'Insurance', 'warranty'],
  // Shapes
  ['circle', CircleIcon, 'Circle', 'round disc disk ring'],
  ['square', SquareIcon, 'Square', 'plate tile'],
  [
    'rectangle-horizontal',
    RectangleHorizontalIcon,
    'Wide rectangle',
    'sheet bar strip plate',
  ],
  ['rectangle-vertical', RectangleVerticalIcon, 'Tall rectangle', 'card panel'],
  ['triangle', TriangleIcon, 'Triangle', 'wedge gusset'],
  [
    'triangle-right',
    TriangleRightIcon,
    'Right triangle',
    'bracket corner gusset',
  ],
  ['diamond', DiamondIcon, 'Diamond', 'rhombus'],
  ['pentagon', PentagonIcon, 'Pentagon'],
  ['hexagon', HexagonIcon, 'Hexagon', 'hex'],
  ['octagon', OctagonIcon, 'Octagon', 'stop'],
  ['ellipse', EllipseIcon, 'Ellipse', 'oval'],
  ['star', StarIcon, 'Star', 'premium favorite'],
  ['heart', HeartIcon, 'Heart', 'love'],
  ['box', BoxIcon, 'Cube', 'solid'],
  ['cylinder', CylinderIcon, 'Cylinder', 'rod pin dowel spacer standoff tube'],
  ['cone', ConeIcon, 'Cone', 'nozzle funnel taper'],
  ['pyramid', PyramidIcon, 'Pyramid'],
  ['torus', TorusIcon, 'Torus', 'ring washer o-ring donut disc disk'],
  ['angle', AngleIcon, 'Angle', 'corner bevel degree'],
  ['radius', RadiusIcon, 'Radius', 'fillet round'],
  ['diameter', DiameterIcon, 'Diameter', 'bore hole round'],
  ['spline', SplineIcon, 'Curve', 'arc spline'],
];

export const costIcons: CostIconOption[] = entries.map(
  ([name, icon, label, keywords = '']) => ({ name, icon, label, keywords }),
);

export const categoryIcon: Record<Category, string> = {
  Hardware: 'screw',
  Packaging: 'package',
  Consumables: 'droplet',
  Labour: 'hard-hat',
  Shipping: 'truck',
  Other: 'shapes',
};

const byName = new Map(costIcons.map((o) => [o.name, o.icon] as const));

// An unknown or missing name falls back to the category default, then to "shapes".
export function costIconFor(
  icon?: string | null,
  category?: string | null,
): LucideIcon {
  return (
    byName.get(icon ?? '') ??
    byName.get(categoryIcon[category as Category]) ??
    ShapesIcon
  );
}

export function CostItemIcon({
  icon,
  category,
}: {
  icon?: string | null;
  category?: string | null;
}) {
  const Icon = costIconFor(icon, category);
  return (
    <div className="flex size-10 shrink-0 items-center justify-center rounded-md bg-muted text-muted-foreground">
      <Icon className="size-5" />
    </div>
  );
}
