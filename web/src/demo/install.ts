// Entry for `vite --mode demo` (main.tsx imports it before rendering): the real app, served by an in-browser
// API over a synthetic farm, embedded by the marketing site at /app-preview/. CI proves the production bundle
// holds none of it by searching for the demo user's email (farm.ts).
import { router } from '@/app/routes';
import { handle } from './api';
import { printers } from './farm';
import { printerToClear, setPaused, startSimulator } from './simulator';

const realFetch = window.fetch.bind(window);
window.fetch = (input: RequestInfo | URL, init?: RequestInit) => {
  const url = new URL(
    input instanceof Request ? input.url : String(input),
    window.location.href,
  );
  if (!url.pathname.startsWith('/api/')) return realFetch(input, init);
  return handle(input instanceof Request ? input : new Request(url, init));
};

try {
  // Dark unless the visitor already chose (next-themes' storage key).
  if (!localStorage.getItem('theme')) localStorage.setItem('theme', 'dark');
} catch {
  // Storage can be blocked; the app then follows the system theme.
}

// --- the marketing page drives the demo through postMessage --------------------------------------------------
const tourStyle = document.createElement('style');
tourStyle.textContent = `
  [data-demo-spotlight] { outline: 2px solid #47bfff !important; outline-offset: 3px; animation: demo-spotlight 1.4s ease-out 3; }
  @keyframes demo-spotlight { from { box-shadow: 0 0 0 0 rgb(71 191 255 / 0.5); } to { box-shadow: 0 0 0 14px rgb(71 191 255 / 0); } }
  @media (prefers-reduced-motion: reduce) { [data-demo-spotlight] { animation: none; } }
`;
document.head.append(tourStyle);

const byText = (selector: string, text: string) => () =>
  [...document.querySelectorAll<HTMLElement>(selector)].find((el) =>
    el.textContent?.trim().startsWith(text),
  ) ?? null;

type Step = { path: () => string; target: () => Element | null };
const TOUR: Record<string, Step> = {
  'clear-plate': {
    path: () => {
      printerToClear();
      return '/';
    },
    target: () =>
      document.querySelector('[aria-label^="Mark "][aria-label$=" ready"]'),
  },
  'why-waiting': {
    path: () => '/jobs',
    target: () =>
      byText('button', 'Waiting for printer')() ??
      byText('button', 'Matching printer available')(),
  },
  'plan-run': {
    path: () => '/production-runs/new',
    target: byText('button', 'Add part or assembly'),
  },
  filament: {
    path: () => '/filament',
    target: () => document.querySelector('main [data-slot="card"]'),
  },
  printer: {
    path: () => `/printers/${printers.find((p) => p.name === 'P1S-01')?.id}`,
    target: () => byText('[data-slot="card"]', 'Feed units')(),
  },
  stats: {
    path: () => '/stats',
    target: () => byText('[data-slot="card"]', 'Prints per day')(),
  },
};

let spotlightTimer: ReturnType<typeof setTimeout> | undefined;
/** Scrolls the target into view inside the demo only: scrollIntoView would also scroll the marketing page. */
function reveal(target: Element) {
  const smooth = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    ? 'auto'
    : 'smooth';
  for (
    let el = target.parentElement;
    el && el !== document.body;
    el = el.parentElement
  ) {
    if (
      !/(auto|scroll)/.test(getComputedStyle(el).overflowY) ||
      el.scrollHeight <= el.clientHeight
    )
      continue;
    const r = target.getBoundingClientRect();
    const c = el.getBoundingClientRect();
    el.scrollBy({
      top: r.top - c.top - (c.height - r.height) / 2,
      behavior: smooth,
    });
  }
  const r = target.getBoundingClientRect();
  if (r.top < 0 || r.bottom > window.innerHeight)
    window.scrollBy({
      top: r.top - (window.innerHeight - r.height) / 2,
      behavior: smooth,
    });
}
function spotlight(find: () => Element | null, attempt = 0) {
  const target = find();
  if (!target) {
    if (attempt < 40) setTimeout(() => spotlight(find, attempt + 1), 150);
    return;
  }
  for (const el of document.querySelectorAll('[data-demo-spotlight]'))
    el.removeAttribute('data-demo-spotlight');
  target.setAttribute('data-demo-spotlight', '');
  reveal(target);
  clearTimeout(spotlightTimer);
  spotlightTimer = setTimeout(
    () => target.removeAttribute('data-demo-spotlight'),
    6000,
  );
}

const toParent = (message: Record<string, unknown>) => {
  if (window.parent !== window)
    window.parent.postMessage({ source: 'openfarm-demo', ...message }, '*');
};
const routeOf = () => router.state.location.pathname;
router.subscribe((state) =>
  toParent({ type: 'route', path: state.location.pathname }),
);
window.addEventListener('message', (event) => {
  if (event.source !== window.parent || event.data?.source !== 'openfarm-site')
    return;
  const { type, step, path } = event.data as {
    type: string;
    step?: string;
    path?: string;
  };
  if (type === 'pause' || type === 'resume') setPaused(type === 'pause');
  if (type === 'navigate' && path?.startsWith('/')) void router.navigate(path);
  if (type === 'tour' && step && TOUR[step]) {
    const path = TOUR[step].path();
    if (routeOf() !== path) void router.navigate(path);
    spotlight(TOUR[step].target);
  }
});
document.addEventListener('pointerdown', () => {
  for (const el of document.querySelectorAll('[data-demo-spotlight]'))
    el.removeAttribute('data-demo-spotlight');
});

startSimulator();
toParent({ type: 'ready', path: routeOf() });
