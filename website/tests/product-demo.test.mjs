import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { JSDOM } from 'jsdom';
import { mountProductDemo } from '../src/scripts/product-demo.ts';

// Exercise the actual generated markup, not a second copy of the component.
const html = readFileSync(new URL('../dist/index.html', import.meta.url), 'utf8');

function fixture(t, enhance = true) {
  const dom = new JSDOM(html, { url: 'https://getopenfarm.github.io/' });
  const doc = dom.window.document;
  const root = doc.querySelector('[data-product-demo]');
  const dispose = enhance ? mountProductDemo(root) : () => {};
  t.after(() => { dispose(); dom.window.close(); });
  const query = selector => root.querySelector(selector);
  const click = selector => query(selector).click();
  const key = (selector, key) => query(selector).dispatchEvent(new dom.window.KeyboardEvent('keydown', { key, bubbles: true }));
  return { dom, doc, root, query, click, key, dispose };
}

test('the unenhanced page has a readable fleet and disabled demo actions', t => {
  const { query, root, doc } = fixture(t, false);
  assert.equal(query('[data-demo-panel="dashboard"]').hidden, false);
  assert.equal(root.querySelectorAll('.demo-printer').length, 3);
  assert.equal(query('[data-demo-ready]').disabled, true);
  assert.equal(doc.querySelector('[data-tour-view]').disabled, true);
  assert.equal(doc.querySelector('h1').textContent.includes('3D print farm management'), true);
});

test('tabs support keyboard navigation, wrapping, Home/End, and one focusable tab', t => {
  const { doc, root, query, key } = fixture(t);
  query('[data-demo-view="dashboard"]').focus();
  key('[data-demo-view="dashboard"]', 'ArrowDown');
  assert.equal(root.dataset.view, 'queue');
  assert.equal(doc.activeElement, query('[data-demo-view="queue"]'));
  assert.equal(query('[data-demo-panel="dashboard"]').hidden, true);
  assert.equal(query('[data-demo-panel="queue"]').hidden, false);
  key('[data-demo-view="queue"]', 'End');
  assert.equal(root.dataset.view, 'filament');
  key('[data-demo-view="filament"]', 'ArrowRight');
  assert.equal(root.dataset.view, 'dashboard');
  key('[data-demo-view="dashboard"]', 'ArrowLeft');
  assert.equal(root.dataset.view, 'filament');
  key('[data-demo-view="filament"]', 'Home');
  assert.equal(root.dataset.view, 'dashboard');
  assert.equal(root.querySelectorAll('[role="tab"][tabindex="0"]').length, 1);
  assert.equal(root.querySelectorAll('[role="tab"][aria-selected="true"]').length, 1);
});

test('the queue tour opens diagnosis and marking ready updates the shared fleet state', t => {
  const { doc, root, query, click } = fixture(t);
  doc.querySelector('[data-tour-view="queue"]').click();
  assert.equal(root.dataset.view, 'queue');
  assert.equal(query('#demo-diagnosis').hidden, false);
  assert.equal(query('[data-demo-diagnose]').getAttribute('aria-expanded'), 'true');
  click('#demo-diagnosis [data-demo-ready]');
  assert.equal(root.dataset.printing, 'true');
  assert.equal(query('[data-base-status]').textContent, 'Printing · 12%');
  assert.equal(query('[data-p1s-status]').textContent, 'Printing');
  assert.equal(query('[data-clearance-count]').textContent, '0');
  assert.equal(query('[data-queued-count]').textContent, '0');
  assert.equal(query('[data-printing-count]').textContent, '2');
  assert.match(query('[data-demo-announcement]').textContent, /now printing in this preview/);
  click('[data-demo-view="dashboard"]');
  assert.match(query('[data-p1s-job]').textContent, /Enclosure base/);
  click('[data-demo-ready]');
  assert.equal(root.dataset.view, 'queue');
  assert.equal(query('#demo-diagnosis').hidden, false);
});

test('diagnosis closes with Escape or Close and returns focus to its trigger', t => {
  const { doc, query, click, key } = fixture(t);
  click('[data-demo-view="queue"]');
  click('[data-demo-diagnose]');
  key('#demo-diagnosis [data-demo-ready]', 'Escape');
  assert.equal(query('#demo-diagnosis').hidden, true);
  assert.equal(doc.activeElement, query('[data-demo-diagnose]'));
  assert.equal(query('[data-demo-diagnose]').getAttribute('aria-expanded'), 'false');
  click('[data-demo-diagnose]');
  click('[data-demo-close-diagnosis]');
  assert.equal(query('#demo-diagnosis').hidden, true);
  assert.equal(doc.activeElement, query('[data-demo-diagnose]'));
});

test('set quantity changes component totals, plate rounding, and accessible output', t => {
  const { dom, root, query, click } = fixture(t);
  click('[data-demo-view="runs"]');
  const range = query('#demo-set-quantity');
  for (const [sets, plates] of [[1, 2], [5, 4], [24, 12]]) {
    range.value = String(sets);
    range.dispatchEvent(new dom.window.Event('input', { bubbles: true }));
    assert.equal(query('[data-run-components]').textContent, `${sets * 2} units`);
    assert.equal(root.querySelectorAll('[data-run-plates] > span').length, plates);
    assert.equal(query('[data-run-plates]').getAttribute('aria-label'), `${plates} planned plates`);
    assert.match(range.getAttribute('aria-valuetext'), new RegExp(`^${sets} complete`));
  }
});

test('spool selection updates inventory, physical color, and meter semantics', t => {
  const { root, query, click } = fixture(t);
  click('[data-demo-view="filament"]');
  click('[data-demo-spool="blue"]');
  assert.equal(query('[data-spool-name]').textContent, 'PLA · Blue');
  assert.equal(query('[data-spool-weight]').textContent, '420 g');
  assert.equal(query('[data-spool-meter]').getAttribute('aria-valuenow'), '420');
  assert.equal(query('[data-spool-meter-fill]').style.width, '42%');
  assert.equal(query('[data-spool-art]').style.getPropertyValue('--spool-color'), '#2563eb');
  assert.equal(root.querySelectorAll('[data-demo-spool][aria-pressed="true"]').length, 1);
  click('[data-demo-view="dashboard"]');
  click('[data-demo-view="filament"]');
  assert.equal(query('[data-spool-name]').textContent, 'PLA · Blue');
});

test('theme changes stay inside the demo and reset restores data without moving the visitor', t => {
  const { dom, doc, root, query, click } = fixture(t);
  click('[data-demo-theme-toggle]');
  assert.equal(root.dataset.demoTheme, 'dark');
  assert.match(query('[data-demo-theme-toggle]').getAttribute('aria-label'), /light mode/);
  assert.equal(doc.documentElement.getAttribute('data-demo-theme'), null);
  click('[data-demo-ready]');
  click('[data-demo-view="runs"]');
  query('#demo-set-quantity').value = '24';
  query('#demo-set-quantity').dispatchEvent(new dom.window.Event('input'));
  click('[data-demo-reset]');
  assert.equal(root.dataset.view, 'runs');
  assert.equal(root.dataset.demoTheme, 'dark');
  assert.equal(root.dataset.printing, 'false');
  assert.equal(query('#demo-set-quantity').value, '8');
  assert.equal(query('[data-run-components]').textContent, '16 units');
  assert.equal(query('[data-spool-name]').textContent, 'PLA · Black');
  assert.match(query('[data-demo-announcement]').textContent, /original sample data/);
});

test('disposing the preview removes listeners', t => {
  const { root, click, dispose } = fixture(t);
  dispose();
  click('[data-demo-ready]');
  assert.equal(root.dataset.printing, 'false');
});

test('the initial theme follows the system and tab orientation follows the viewport', t => {
  const { dom, root, query } = fixture(t, false);
  let narrow = true;
  const listeners = new Set();
  dom.window.matchMedia = media => ({
    get matches() { return media.includes('max-width') ? narrow : true; },
    addEventListener: (_, listener) => listeners.add(listener),
    removeEventListener: (_, listener) => listeners.delete(listener),
  });
  const dispose = mountProductDemo(root);
  try {
    assert.equal(root.dataset.demoTheme, 'dark');
    assert.equal(query('[role="tablist"]').getAttribute('aria-orientation'), 'horizontal');
    narrow = false;
    listeners.forEach(listener => listener());
    assert.equal(query('[role="tablist"]').getAttribute('aria-orientation'), 'vertical');
  } finally {
    dispose();
  }
  assert.equal(listeners.size, 0);
});
