import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import {
  type Ability,
  describeArea,
  levelLeaves,
  levelOf,
  levelsFor,
  wording,
} from './describe';

// The real catalog, read from the C# source so these checks cover every area
// the API serves rather than a hand-made copy.
const source = readFileSync(
  path.resolve(
    __dirname,
    '../../../../api/src/OpenFarm.Application/Auth/PermissionCatalog.cs',
  ),
  'utf8',
);
const areas = [
  ...source.matchAll(/new\("([a-z-]+)", "([^"]+)", "([^"]+)", (true|false)\)/g),
].map(([, key, label, noun, adminOnly]) => ({
  key,
  label,
  noun,
  administratorOnly: adminOnly === 'true',
}));
const catalog: Ability[] = [
  ...source.matchAll(
    /new\("([a-z-]+:[a-z:-]+)", "([a-z-]+)", "([^"]+)", "([^"]+)", (true|false), (true|false), (true|false)(, IsExplicit: true)?(?:, Verb: "([^"]+)")?(?:, Phrase: "([^"]+)")?\)/g,
  ),
].map(([, key, area, label, description, r, s, d, x, verb, phrase]) => ({
  key,
  area,
  label,
  description,
  isRead: r === 'true',
  isSensitive: s === 'true',
  isDestructive: d === 'true',
  isExplicit: x !== undefined,
  verb: verb ?? null,
  phrase: phrase ?? null,
}));
const leavesOf = (area: string) => catalog.filter((l) => l.area === area);
const keys = (leaves: Ability[]) => leaves.map((l) => l.key);
const noun = (area: string) => areas.find((a) => a.key === area)?.noun ?? '';

describe('catalog fixture', () => {
  it('reads every area and leaf from the C# catalog', () => {
    expect(areas.length).toBeGreaterThanOrEqual(16);
    expect(catalog.length).toBe(source.match(/^ {8}new\("[a-z-]+:/gm)?.length);
  });
});

describe('access levels', () => {
  it('offers only the levels that differ', () => {
    expect(levelsFor(leavesOf('tags'))).toEqual(['none', 'view']);
    expect(levelsFor(leavesOf('activity-log'))).toEqual(['none']);
    expect(levelsFor(leavesOf('settings'))).toEqual(['none', 'view', 'edit']);
    expect(levelsFor(leavesOf('parts'))).toEqual([
      'none',
      'view',
      'edit',
      'full',
    ]);
  });

  it('puts destructive abilities in Full and never an explicit one in any level', () => {
    expect(keys(levelLeaves(leavesOf('parts'), 'edit'))).toEqual([
      'parts:list',
      'parts:view',
      'parts:read',
      'parts:create',
      'parts:edit',
      'parts:replace-file',
    ]);
    expect(keys(levelLeaves(leavesOf('parts'), 'full'))).toContain(
      'parts:delete',
    );
    for (const level of ['view', 'edit', 'full'] as const) {
      expect(keys(levelLeaves(leavesOf('printers'), level))).not.toContain(
        'printers:edit-connection',
      );
      expect(keys(levelLeaves(leavesOf('print-jobs'), level))).not.toContain(
        'print-jobs:remote-control',
      );
    }
  });

  it('reads each level back from its own leaves, in every area', () => {
    for (const { key } of areas) {
      const leaves = leavesOf(key);
      for (const level of levelsFor(leaves))
        expect(levelOf(leaves, keys(levelLeaves(leaves, level)))).toBe(level);
    }
  });

  it('calls anything else custom, and ignores explicit abilities', () => {
    const printers = leavesOf('printers');
    expect(levelOf(printers, ['printers:register'])).toBe('custom');
    expect(
      levelOf(printers, [
        ...keys(levelLeaves(printers, 'view')),
        'printers:edit-connection',
      ]),
    ).toBe('view');
  });

  it('gives every leaf words to be described with', () => {
    for (const { key, noun } of areas)
      expect(
        describeArea(leavesOf(key), noun, keys(leavesOf(key))),
      ).not.toMatch(/undefined|null/);
  });

  it('never repeats a verb within an area', () => {
    for (const { key } of areas) {
      const words = leavesOf(key).map(wording);
      expect(new Set(words).size, key).toBe(words.length);
      const verbs = leavesOf(key).flatMap((l) => (l.verb ? [l.verb] : []));
      expect(new Set(verbs).size, key).toBe(verbs.length);
    }
  });
});

describe('summary sentences', () => {
  it('names what is allowed, then what is not', () => {
    const parts = leavesOf('assemblies');
    expect(
      describeArea(parts, noun('assemblies'), keys(levelLeaves(parts, 'edit'))),
    ).toBe('Can see, add and change assemblies. Cannot delete.');
    expect(describeArea(leavesOf('tags'), noun('tags'), ['tags:list'])).toBe(
      'Can see tags.',
    );
    expect(describeArea(leavesOf('parts'), noun('parts'), [])).toBe(
      'No access.',
    );
  });

  it('adds a standalone phrase and never lists an explicit ability as missing', () => {
    const jobs = leavesOf('print-jobs');
    expect(
      describeArea(jobs, noun('print-jobs'), [
        'print-jobs:view',
        'print-jobs:remote-control',
      ]),
    ).toBe(
      'Can see print jobs and control prints remotely. Cannot pin, reorder, requeue, update job status, cancel or write off.',
    );
    expect(
      describeArea(jobs, noun('print-jobs'), keys(levelLeaves(jobs, 'full'))),
    ).toBe(
      'Can see, pin, reorder, requeue, cancel and write off print jobs and update job status.',
    );
  });
});
