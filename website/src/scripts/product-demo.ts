import { demoSpools, demoViews } from '../lib/demo-data.ts';

type View = typeof demoViews[number]['id'];

/** An isolated product walkthrough. All changes live in this component's DOM. */
export function mountProductDemo(root: HTMLElement) {
  const scope = root.closest<HTMLElement>('.product-showcase') ?? root;
  const doc = root.ownerDocument;
  const win = doc.defaultView;
  const tabs = Array.from(root.querySelectorAll<HTMLButtonElement>('[data-demo-view]'));
  const panels = Array.from(root.querySelectorAll<HTMLElement>('[data-demo-panel]'));
  const tours = Array.from(scope.querySelectorAll<HTMLButtonElement>('[data-tour-view]'));
  const query = <T extends HTMLElement = HTMLElement>(selector: string): T => {
    const element = root.querySelector<T>(selector);
    if (!element) throw new Error(`Missing preview element: ${selector}`);
    return element;
  };
  const text = (selector: string, value: string) => root.querySelectorAll(selector).forEach(element => { element.textContent = value; });
  const announce = (message: string) => { query('[data-demo-announcement]').textContent = message; };
  const range = query<HTMLInputElement>('#demo-set-quantity');
  const diagnosis = query('#demo-diagnosis');
  let view: View = 'dashboard';
  let started = false;
  let selectedSpool: string = 'black';
  let dark = win?.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false;

  function activate(next: View, focusTab = false) {
    if (!demoViews.some(item => item.id === next)) return;
    view = next;
    root.dataset.view = next;
    tabs.forEach(tab => {
      const selected = tab.dataset.demoView === next;
      tab.setAttribute('aria-selected', String(selected));
      tab.tabIndex = selected ? 0 : -1;
      if (selected && focusTab) tab.focus();
    });
    panels.forEach(panel => { panel.hidden = panel.dataset.demoPanel !== next; });
    tours.forEach(tour => tour.setAttribute('aria-pressed', String(tour.dataset.tourView === next)));
    const current = demoViews.find(item => item.id === next)!;
    text('[data-current-view]', current.label);
    text('[data-demo-hint]', current.hint);
  }

  function showDiagnosis(open: boolean, focusTrigger = false) {
    diagnosis.hidden = !open;
    const trigger = query<HTMLButtonElement>('[data-demo-diagnose]');
    trigger.setAttribute('aria-expanded', String(open));
    if (focusTrigger) trigger.focus();
  }

  function renderPrinter() {
    root.dataset.printing = String(started);
    text('[data-printing-count]', started ? '2' : '1');
    text('[data-clearance-count]', started ? '0' : '1');
    text('[data-queued-count]', started ? '0' : '1');
    text('[data-p1s-status]', started ? 'Printing' : 'Awaiting clearance');
    text('[data-base-status]', started ? 'Printing · 12%' : 'Awaiting clearance');
    text('[data-p1s-job]', started ? 'Enclosure base · Plate 1' : 'Ready for its next plate');
    text('[data-ready-label]', started ? 'View print' : 'Mark ready');
    text('[data-diagnose-label]', started ? 'View print' : 'View diagnosis');
    text('[data-readiness-symbol]', started ? '✓' : '!');
    text('[data-readiness-description]', started ? 'Printer ready · Job started' : 'Bed clearance needed');
    query('[data-p1s-status]').dataset.status = started ? 'printing' : 'attention';
    query('[data-base-status]').dataset.status = started ? 'printing' : 'attention';
    query('.demo-p1s-art .printer-drawing').classList.toggle('printer-drawing-active', started);
  }

  function renderPlan() {
    const sets = Math.max(1, Math.min(24, Math.round(Number(range.value) || 8)));
    const plates = Math.ceil(sets / 4) * 2;
    range.value = String(sets);
    range.setAttribute('aria-valuetext', `${sets} complete ${sets === 1 ? 'set' : 'sets'}`);
    text('[data-run-sets]', `${sets} ${sets === 1 ? 'set' : 'sets'}`);
    text('[data-run-units]', `${sets} ${sets === 1 ? 'unit' : 'units'}`);
    text('[data-run-components]', `${sets * 2} units`);
    text('[data-run-plates-label]', `${plates} whole plates`);
    const strip = query('[data-run-plates]');
    strip.setAttribute('aria-label', `${plates} planned plates`);
    strip.replaceChildren(...Array.from({ length: plates }, () => doc.createElement('span')));
  }

  function renderSpool() {
    const spool = demoSpools.find(item => item.id === selectedSpool)!;
    root.querySelectorAll<HTMLButtonElement>('[data-demo-spool]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.demoSpool === spool.id)));
    text('[data-spool-name]', spool.name);
    text('[data-spool-location]', spool.printer);
    text('[data-spool-weight]', `${spool.remaining} g`);
    query('[data-spool-art]').style.setProperty('--spool-color', spool.color);
    query('[data-spool-meter]').setAttribute('aria-valuenow', String(spool.remaining));
    query('[data-spool-meter-fill]').style.width = `${spool.remaining / 10}%`;
  }

  function renderTheme() {
    root.dataset.demoTheme = dark ? 'dark' : 'light';
    text('[data-theme-label]', dark ? 'Light mode' : 'Dark mode');
    query('[data-demo-theme-toggle]').setAttribute('aria-label', `Switch preview to ${dark ? 'light' : 'dark'} mode`);
  }

  function click(event: Event) {
    const button = (event.target as Element | null)?.closest<HTMLButtonElement>('button');
    if (!button || !scope.contains(button) || button.disabled) return;
    const next = button.dataset.demoView ?? button.dataset.demoShortcut ?? button.dataset.tourView;
    if (next) {
      activate(next as View);
      if (button.dataset.tourView === 'queue') showDiagnosis(true);
    } else if (button.hasAttribute('data-demo-diagnose')) {
      showDiagnosis(diagnosis.hidden);
    } else if (button.hasAttribute('data-demo-close-diagnosis')) {
      showDiagnosis(false, true);
    } else if (button.hasAttribute('data-demo-ready')) {
      if (started) {
        activate('queue');
        showDiagnosis(true);
      } else {
        started = true;
        renderPrinter();
        announce('Workshop P1S is ready. Enclosure base is now printing in this preview.');
      }
    } else if (button.dataset.demoSpool && demoSpools.some(spool => spool.id === button.dataset.demoSpool)) {
      selectedSpool = button.dataset.demoSpool;
      renderSpool();
      announce(`${query('[data-spool-name]').textContent}, ${query('[data-spool-weight]').textContent} remaining.`);
    } else if (button.hasAttribute('data-demo-theme-toggle')) {
      dark = !dark;
      renderTheme();
    } else if (button.hasAttribute('data-demo-reset')) {
      started = false;
      selectedSpool = 'black';
      range.value = '8';
      renderPrinter();
      renderPlan();
      renderSpool();
      showDiagnosis(false);
      announce('Preview reset. The original sample data is restored.');
    }
  }

  function keydown(event: KeyboardEvent) {
    if (event.key === 'Escape' && !diagnosis.hidden && view === 'queue') {
      event.preventDefault();
      showDiagnosis(false, true);
      return;
    }
    const tab = (event.target as Element | null)?.closest<HTMLButtonElement>('[data-demo-view]');
    if (!tab) return;
    const index = tabs.indexOf(tab);
    let next = index;
    if (event.key === 'ArrowRight' || event.key === 'ArrowDown') next = (index + 1) % tabs.length;
    else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') next = (index + tabs.length - 1) % tabs.length;
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = tabs.length - 1;
    else return;
    event.preventDefault();
    activate(tabs[next].dataset.demoView as View, true);
  }

  const narrow = win?.matchMedia?.('(max-width: 720px)');
  const orientation = () => query('[role="tablist"]').setAttribute('aria-orientation', narrow?.matches ? 'horizontal' : 'vertical');
  scope.querySelectorAll<HTMLButtonElement>('button[disabled]').forEach(button => { button.disabled = false; });
  range.disabled = false;
  activate('dashboard');
  renderPrinter();
  renderPlan();
  renderSpool();
  renderTheme();
  orientation();
  scope.addEventListener('click', click);
  root.addEventListener('keydown', keydown);
  range.addEventListener('input', renderPlan);
  narrow?.addEventListener('change', orientation);

  return () => {
    scope.removeEventListener('click', click);
    root.removeEventListener('keydown', keydown);
    range.removeEventListener('input', renderPlan);
    narrow?.removeEventListener('change', orientation);
  };
}
