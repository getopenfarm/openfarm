// Seed data for the Assemblies (bills-of-materials) feature — no backend
// exists yet. Components reference real parts from the Parts library's mock
// store by name at module-eval time (the same cross-referencing pattern the
// Parts/Filament features use); if a named part isn't found, fall back to
// any available part rather than crashing.
import { partsStore } from '@/features/assemblies/data/mockPartsStore';
import type { Assembly } from './mockAssembliesStore';

function findPart(name: string) {
  const parts = partsStore.getSnapshot();
  return parts.find((part) => part.name === name) ?? parts[0];
}

const organizerTray = findPart('Gridfinity 2×2 bin');
const organizerDivider = findPart('Cable clip');
const organizerPenHolder = findPart('Phone stand');

const storageHinge = findPart('Enclosure bracket');
const storageLid = findPart('Sign panel');
const storageLatch = findPart('Cable clip');

const keyboardKeycap = findPart('Cherry keycap');
const keyboardStabilizer = findPart('Spur gear');
const keyboardCaseBracket = findPart('Enclosure bracket');

const planterPot = findPart('Planter pot');

export const HINGED_STORAGE_BOX_ID = 'assembly-hinged-storage-box';

export const SEED_ASSEMBLIES: Assembly[] = [
  {
    id: 'assembly-desktop-organizer-kit',
    name: 'Desktop organizer kit',
    description: 'A tray, dividers, and a pen holder for a tidy desk.',
    components: [
      {
        id: 'component-organizer-tray',
        partId: organizerTray?.id,
        quantity: 1,
      },
      {
        id: 'component-organizer-divider',
        partId: organizerDivider?.id,
        quantity: 4,
      },
      {
        id: 'component-organizer-pen-holder',
        partId: organizerPenHolder?.id,
        quantity: 1,
      },
    ],
  },
  {
    id: HINGED_STORAGE_BOX_ID,
    name: 'Hinged storage box',
    description: 'A lidded box with printed hinges and latches.',
    components: [
      { id: 'component-storage-hinge', partId: storageHinge?.id, quantity: 1 },
      { id: 'component-storage-lid', partId: storageLid?.id, quantity: 1 },
      { id: 'component-storage-latch', partId: storageLatch?.id, quantity: 2 },
    ],
  },
  {
    id: 'assembly-keyboard-set',
    name: 'Keyboard set',
    description:
      'Keycaps, stabilizers, and a case bracket for a custom keyboard build.',
    components: [
      {
        id: 'component-keyboard-keycap',
        partId: keyboardKeycap?.id,
        quantity: 61,
      },
      {
        id: 'component-keyboard-stabilizer',
        partId: keyboardStabilizer?.id,
        quantity: 8,
      },
      {
        id: 'component-keyboard-case-bracket',
        partId: keyboardCaseBracket?.id,
        quantity: 2,
      },
    ],
  },
  {
    id: 'assembly-planter-bundle',
    name: 'Planter bundle',
    description:
      'A pair of planter pots bundled with a storage box for tools and soil.',
    components: [
      { id: 'component-planter-pot', partId: planterPot?.id, quantity: 2 },
      // The one nested sub-assembly component in the seed data — reuses
      // "Hinged storage box" (assembly #2 above) as a component of this one.
      {
        id: 'component-planter-storage',
        subAssemblyId: HINGED_STORAGE_BOX_ID,
        quantity: 3,
      },
    ],
  },
];
