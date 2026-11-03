// Drives the embedded demo (web/src/demo/install.ts) over postMessage: tour chips and "Show in demo"
// buttons navigate it, the active chip follows its route, and its clock pauses while it is off screen.
const TOUR_ROUTES: Record<string, (path: string) => boolean> = {
  'clear-plate': (path) => path === '/',
  'why-waiting': (path) => path === '/jobs',
  'plan-run': (path) => path === '/production-runs/new',
  filament: (path) => path.startsWith('/filament'),
  printer: (path) => path.startsWith('/printers/'),
  stats: (path) => path === '/stats',
};

export function mountLiveDemo(root: HTMLElement) {
  const frame = root.querySelector('iframe');
  const view = root.querySelector('[data-demo-view]');
  const base = root.dataset.src;
  if (!frame || !view || !base) return;
  const origin = new URL(base, window.location.href).origin;
  const chips = [...document.querySelectorAll<HTMLButtonElement>('.tour [data-tour]')];
  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
  const send = (message: Record<string, unknown>) =>
    frame.contentWindow?.postMessage({ source: 'openfarm-site', ...message }, origin);
  const loaded = () => view.classList.add('is-loaded');

  window.addEventListener('message', (event) => {
    if (event.source !== frame.contentWindow || event.data?.source !== 'openfarm-demo') return;
    const { type, path } = event.data as { type: string; path?: string };
    if (type === 'ready') loaded();
    if (path) for (const chip of chips) chip.setAttribute('aria-pressed', String(TOUR_ROUTES[chip.dataset.tour ?? '']?.(path) ?? false));
  });
  frame.addEventListener('load', () => setTimeout(loaded, 1500));

  const bringIntoView = () => {
    const box = root.getBoundingClientRect();
    if (box.top < 64 || box.bottom > window.innerHeight)
      root.scrollIntoView({ behavior: reduced.matches ? 'auto' : 'smooth', block: 'center' });
  };
  document.addEventListener('click', (event) => {
    const target = (event.target as Element | null)?.closest<HTMLElement>('[data-tour], [data-show]');
    if (!target) return;
    if (target.dataset.tour) send({ type: 'tour', step: target.dataset.tour });
    else send({ type: 'navigate', path: target.dataset.show });
    bringIntoView();
  });

  root.querySelector('[data-demo-reset]')?.addEventListener('click', () => {
    view.classList.remove('is-loaded');
    frame.src = `${base}?reset=${Date.now()}#/`;
  });

  // The farm only moves while you can see it.
  new IntersectionObserver(([entry]) => send({ type: entry.isIntersecting ? 'resume' : 'pause' }), { threshold: 0.15 }).observe(root);
}

/** Copy buttons for the install commands (prompts and output excluded). */
export function mountCopyButtons() {
  for (const button of document.querySelectorAll<HTMLButtonElement>('[data-copy]')) {
    const code = button.closest('figure')?.querySelector('code');
    const label = button.querySelector('span');
    if (!code || !label) continue;
    button.addEventListener('click', async () => {
      const text = [...code.querySelectorAll<HTMLElement>('[data-line]')].map((line) => line.dataset.line).join('\n');
      try {
        await navigator.clipboard.writeText(text);
        label.textContent = 'Copied';
      } catch {
        label.textContent = 'Copy failed';
      }
      setTimeout(() => {
        label.textContent = 'Copy';
      }, 2000);
    });
  }
}
