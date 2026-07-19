import { describe, expect, it } from 'vitest';
import { partsStore } from '@/features/assemblies/data/mockPartsStore';
import type { Assembly } from '../data/mockAssembliesStore';
import { computeAssemblyTotals } from './computeAssemblyTotals';

describe('computeAssemblyTotals', () => {
  it('sums direct part components by quantity', () => {
    const [partA, partB] = partsStore.getSnapshot();

    const assembly: Assembly = {
      id: 'assembly-test-direct',
      name: 'Direct parts only',
      description: '',
      components: [
        { id: 'c1', partId: partA.id, quantity: 2 },
        { id: 'c2', partId: partB.id, quantity: 3 },
      ],
    };

    const totals = computeAssemblyTotals(assembly, [assembly]);

    expect(totals.totalParts).toBe(5);
    expect(totals.estimatedTimeMinutes).toBe(
      partA.estimatedTimeMinutes * 2 + partB.estimatedTimeMinutes * 3,
    );
  });

  it('ignores components that reference a missing part or sub-assembly', () => {
    const assembly: Assembly = {
      id: 'assembly-test-missing',
      name: 'Missing references',
      description: '',
      components: [
        { id: 'c1', partId: 'part-does-not-exist', quantity: 5 },
        { id: 'c2', subAssemblyId: 'assembly-does-not-exist', quantity: 5 },
      ],
    };

    const totals = computeAssemblyTotals(assembly, [assembly]);

    expect(totals).toEqual({ totalParts: 0, estimatedTimeMinutes: 0 });
  });

  it('resolves a nested sub-assembly component exactly one level deep, never recursing further', () => {
    const [partA, partB, partC] = partsStore.getSnapshot();

    // A "grandchild" assembly that must never be reached by the parent's
    // computation. It carries a huge quantity so that if the computation
    // ever recursed past one level, the assertions below would fail loudly.
    const grandchild: Assembly = {
      id: 'assembly-test-grandchild',
      name: 'Grandchild',
      description: '',
      components: [{ id: 'gc1', partId: partC.id, quantity: 999 }],
    };

    const child: Assembly = {
      id: 'assembly-test-child',
      name: 'Child',
      description: '',
      components: [
        { id: 'ch1', partId: partA.id, quantity: 1 },
        // The child itself has a nested sub-assembly component — the
        // parent's computation must NOT recurse into this.
        { id: 'ch2', subAssemblyId: grandchild.id, quantity: 5 },
      ],
    };

    const parent: Assembly = {
      id: 'assembly-test-parent',
      name: 'Parent',
      description: '',
      components: [
        { id: 'p1', partId: partB.id, quantity: 1 },
        { id: 'p2', subAssemblyId: child.id, quantity: 2 },
      ],
    };

    const totals = computeAssemblyTotals(parent, [grandchild, child, parent]);

    // Only child's direct part component (partA × 1) counts, multiplied by
    // the outer component's quantity (2) — the grandchild's contribution
    // (which would add 999 × 5 × 2 parts if wrongly recursed into) must be
    // completely absent from the result.
    const expectedTotalParts = 1 /* partB */ + 1 * 2; /* partA via child */
    const expectedTime =
      partB.estimatedTimeMinutes * 1 + partA.estimatedTimeMinutes * 1 * 2;

    expect(totals.totalParts).toBe(expectedTotalParts);
    expect(totals.estimatedTimeMinutes).toBe(expectedTime);
  });
});
