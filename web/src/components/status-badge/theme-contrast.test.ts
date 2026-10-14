/// <reference types="node" />

import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

const css = readFileSync(resolve(process.cwd(), 'src/index.css'), 'utf8');
function tokens(selector: string) {
  const block = css.split(`${selector} {`)[1]?.split('\n}')[0] ?? '';
  return Object.fromEntries(
    [...block.matchAll(/(--[\w-]+):\s*([^;]+);/g)].map((match) => [
      match[1],
      match[2],
    ]),
  );
}
const light = tokens(':root');
const dark = { ...light, ...tokens('.dark') };
function rgb(token: string, theme: Record<string, string>): number[] {
  const value = theme[token];
  if (!value) throw new Error(`Missing token ${token}`);
  if (value.startsWith('var(')) return rgb(value.slice(4, -1), theme);
  if (!/^#[\da-f]{6}$/i.test(value))
    throw new Error(`Expected a resolved hex color for ${token}: ${value}`);
  return [1, 3, 5].map(
    (offset) => Number.parseInt(value.slice(offset, offset + 2), 16) / 255,
  );
}
function luminance(color: number[]) {
  return color
    .map((channel) =>
      channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4,
    )
    .reduce(
      (sum, channel, index) => sum + channel * [0.2126, 0.7152, 0.0722][index],
      0,
    );
}

describe('status badge text contrast', () => {
  for (const [mode, theme] of Object.entries({ light, dark })) {
    for (const kind of [
      'zinc',
      'green',
      'amber',
      'red',
      'violet',
      'blue',
      'orange',
    ]) {
      it(`${mode} ${kind} meets AA text contrast on supported surfaces`, () => {
        const foreground = rgb(`--kind-${kind}`, theme);
        for (const surface of ['--card', '--background', '--popover']) {
          const background = rgb(surface, theme);
          // Badge backgrounds use the same kind token at 10% alpha.
          const tinted = background.map(
            (channel, index) => channel * 0.9 + foreground[index] * 0.1,
          );
          const a = luminance(foreground);
          const b = luminance(tinted);
          expect(
            (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05),
          ).toBeGreaterThanOrEqual(4.5);
        }
      });
    }
  }
});
