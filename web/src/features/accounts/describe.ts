import type { PermissionDefinition } from '@/lib/api/generated/endpoints.schemas';

/**
 * Access levels and plain-language summaries over one area's abilities
 * (ported from Akaun's describe.ts).
 *
 * A level is a name for a set of one area's abilities, worked out from each
 * ability's tier: nothing is stored as a level. A group or user still holds
 * keys; "Parts: Edit" shows when the keys held are exactly what Edit stands
 * for, and "Custom" otherwise.
 *
 * Explicit abilities belong to no level: picking a level never grants or
 * removes one, and they are left out when deciding the level.
 *
 * Every function takes `leaves`: one area's abilities, in catalog order.
 */

export type Ability = Required<PermissionDefinition>;
export type AccessLevel = 'none' | 'view' | 'edit' | 'full';
export type AreaLevel = AccessLevel | 'custom';
type Tier = 'read' | 'write' | 'full';

export const LEVEL_LABELS: Record<AreaLevel, string> = {
  none: 'None',
  view: 'View',
  edit: 'Edit',
  full: 'Full',
  custom: 'Custom',
};

const TIERS_OF: Record<AccessLevel, readonly Tier[]> = {
  none: [],
  view: ['read'],
  edit: ['read', 'write'],
  full: ['read', 'write', 'full'],
};

const tierOf = (leaf: Ability): Tier =>
  leaf.isRead ? 'read' : leaf.isDestructive ? 'full' : 'write';

/** The abilities a level can grant: every leaf that is not explicit. */
export function levelledLeaves(leaves: Ability[]): Ability[] {
  return leaves.filter((leaf) => !leaf.isExplicit);
}

export function explicitLeaves(leaves: Ability[]): Ability[] {
  return leaves.filter((leaf) => leaf.isExplicit);
}

export function levelLeaves(leaves: Ability[], level: AccessLevel): Ability[] {
  return levelledLeaves(leaves).filter((leaf) =>
    TIERS_OF[level].includes(tierOf(leaf)),
  );
}

/**
 * The levels worth offering, cheapest first. A level that stands for the
 * same abilities as a cheaper one is left out, so Tags offers only None and
 * View.
 */
export function levelsFor(leaves: Ability[]): AccessLevel[] {
  const seen = new Set<string>();
  return (['none', 'view', 'edit', 'full'] as const).filter((level) => {
    const key = levelLeaves(leaves, level)
      .map((leaf) => leaf.key)
      .join(',');
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

/** The level a set of keys stands for in one area, or "custom". */
export function levelOf(leaves: Ability[], keys: Iterable<string>): AreaLevel {
  const held = new Set(keys);
  const own = levelledLeaves(leaves).filter((leaf) => held.has(leaf.key));
  for (const level of levelsFor(leaves)) {
    const want = levelLeaves(leaves, level);
    if (want.length === own.length && want.every((leaf) => held.has(leaf.key)))
      return level;
  }
  return 'custom';
}

/** "a", "a and b", "a, b and c". */
export function listOf(items: string[], conjunction: 'and' | 'or'): string {
  if (items.length <= 1) return items.join('');
  return `${items.slice(0, -1).join(', ')} ${conjunction} ${items.at(-1)}`;
}

export const wording = (leaf: Ability): string =>
  leaf.verb ?? leaf.phrase ?? leaf.label.toLowerCase();

/**
 * One area's abilities as a sentence: "Can see, add and change parts.
 * Cannot delete." An explicit ability is named when held and never listed
 * as missing, since no level would have given it.
 */
export function describeArea(
  leaves: Ability[],
  noun: string,
  keys: Iterable<string>,
): string {
  const held = new Set(keys);
  const on = leaves.filter((leaf) => held.has(leaf.key));
  if (on.length === 0) return 'No access.';

  const verbs = on.filter((leaf) => leaf.verb).map(wording);
  const phrases = on.filter((leaf) => !leaf.verb).map(wording);
  const clauses = [
    ...(verbs.length ? [`${listOf(verbs, 'and')} ${noun}`] : []),
    ...phrases,
  ];
  const can = `Can ${listOf(clauses, 'and')}.`;

  const missing = leaves
    .filter((leaf) => !held.has(leaf.key) && !leaf.isExplicit)
    .map(wording);
  return missing.length ? `${can} Cannot ${listOf(missing, 'or')}.` : can;
}
