import { describe, expect, it } from 'vitest';
import { categoryIcon, costIconFor, costIcons } from './costIcons';
import { categories } from './costing';

const names = costIcons.map((option) => option.name);

describe('cost icons', () => {
  it('has unique names the API accepts', () => {
    expect(new Set(names).size).toBe(names.length);
    expect(
      names.every(
        (name) => /^[a-z0-9]+(-[a-z0-9]+)*$/.test(name) && name.length <= 40,
      ),
    ).toBe(true);
  });

  it('has a default in the library for every category', () => {
    for (const category of categories)
      expect(names).toContain(categoryIcon[category]);
  });

  it('falls back to the category default, then to shapes', () => {
    expect(costIconFor('magnet', 'Shipping')).not.toBe(
      costIconFor(null, 'Shipping'),
    );
    expect(costIconFor('no-such-icon', 'Shipping')).toBe(costIconFor('truck'));
    expect(costIconFor(null, 'Labour')).toBe(costIconFor('hard-hat'));
    expect(costIconFor(null, 'Bogus')).toBe(costIconFor('shapes'));
    expect(costIconFor()).toBe(costIconFor('shapes'));
  });
});
