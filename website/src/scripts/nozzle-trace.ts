// The hero's moving accent: a white-hot nozzle drops in and prints two perimeters around the live-demo window.
// Fresh extrusion glows white, then amber, then cools to the brand cyan and stays as a faint frame. It plays once
// when the window is in view, again on Reset; reduced motion gets the finished frame only.
const SPEED = 2800; // px/s along the path
const STEP = 6; // px between path samples
const COOL_MS = 1600; // white → amber → cyan
const TRAVEL_MS = 380;
const OUTER = 11; // px outside the window frame
const INNER = 5;
const RADIUS = 14; // the window's corner radius
const HEADLINE_MS = 1400; // the headline's print animation

type Sample = { x: number; y: number; s: number; corner: boolean; at?: number };
type Spark = { x: number; y: number; vx: number; vy: number; born: number; life: number };

/** Clockwise from the top-left, after the corner. */
function roundedRect(x: number, y: number, w: number, h: number, r: number, out: Sample[], s0: number) {
  const corners = [
    [x + w - r, y + r, -Math.PI / 2],
    [x + w - r, y + h - r, 0],
    [x + r, y + h - r, Math.PI / 2],
    [x + r, y + r, Math.PI],
  ] as const;
  let s = s0;
  let prev = { x: x + r, y };
  const push = (px: number, py: number, corner = false) => {
    s += Math.hypot(px - prev.x, py - prev.y);
    out.push({ x: px, y: py, s, corner });
    prev = { x: px, y: py };
  };
  out.push({ x: prev.x, y: prev.y, s, corner: false });
  for (const [cx, cy, from] of corners) {
    const ex = cx + r * Math.cos(from);
    const ey = cy + r * Math.sin(from);
    const n = Math.max(1, Math.round(Math.hypot(ex - prev.x, ey - prev.y) / STEP));
    const sx = prev.x;
    const sy = prev.y;
    for (let i = 1; i <= n; i++) push(sx + ((ex - sx) * i) / n, sy + ((ey - sy) * i) / n);
    const arc = Math.max(2, Math.round((r * Math.PI) / 2 / STEP));
    for (let i = 1; i <= arc; i++) {
      const a = from + (Math.PI / 2) * (i / arc);
      push(cx + r * Math.cos(a), cy + r * Math.sin(a), i === arc);
    }
  }
  const n = Math.max(1, Math.round(Math.hypot(x + r - prev.x, y - prev.y) / STEP));
  const sx = prev.x;
  const sy = prev.y;
  for (let i = 1; i <= n; i++) push(sx + ((x + r - sx) * i) / n, sy + ((y - sy) * i) / n);
  return s;
}

/** Trapezoidal velocity, like a printer's acceleration: u in [0, 1] → distance share. */
function accelerate(u: number, ramp = 0.08) {
  const k = 1 / (1 - ramp);
  if (u <= 0) return 0;
  if (u >= 1) return 1;
  if (u < ramp) return (k * u * u) / (2 * ramp);
  if (u > 1 - ramp) return 1 - (k * (1 - u) * (1 - u)) / (2 * ramp);
  return k * (u - ramp / 2);
}

const mix = (a: number[], b: number[], t: number) => a.map((v, i) => Math.round(v + (b[i] - v) * t));
const WHITE = [255, 255, 255];
const AMBER = [255, 181, 71];
const CYAN = [71, 191, 255];
function heatColor(age: number) {
  const t = Math.min(1, age / COOL_MS);
  return t < 0.25 ? mix(WHITE, AMBER, t / 0.25) : mix(AMBER, CYAN, (t - 0.25) / 0.75);
}

export function mountNozzleTrace(canvas: HTMLCanvasElement, frame: HTMLElement) {
  const ctx = canvas.getContext('2d');
  if (!ctx) return { play() {} };
  const residual = document.createElement('canvas');
  const rctx = residual.getContext('2d');
  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
  let path: Sample[] = [];
  let length = 0;
  let from = { x: 0, y: 0 };
  let dpr = 1;
  let start = -1;
  let baked = 0;
  let corner = 0;
  let printed = 0;
  let raf = 0;
  let closedAt = -1;
  let sparks: Spark[] = [];

  function layout() {
    dpr = Math.min(window.devicePixelRatio || 1, 2);
    const box = canvas.getBoundingClientRect();
    canvas.width = residual.width = Math.round(box.width * dpr);
    canvas.height = residual.height = Math.round(box.height * dpr);
    const f = frame.getBoundingClientRect();
    const x = f.left - box.left;
    const y = f.top - box.top;
    path = [];
    const outer = roundedRect(x - OUTER, y - OUTER, f.width + 2 * OUTER, f.height + 2 * OUTER, RADIUS + OUTER, path, 0);
    length = roundedRect(x - INNER, y - INNER, f.width + 2 * INNER, f.height + 2 * INNER, RADIUS + INNER, path, outer + OUTER - INNER);
    from = { x: x + f.width * 0.62, y: Math.max(12, y - 190) };
    baked = 0;
  }

  function stroke(target: CanvasRenderingContext2D, from: number, to: number, rgb: number[], alpha: number, width: number) {
    if (to - from < 1) return;
    target.beginPath();
    target.moveTo(path[from].x, path[from].y);
    for (let i = from + 1; i <= to; i++) {
      // A new loop is a separate line (the hop between perimeters is a travel move).
      if (path[i].s - path[i - 1].s > STEP * 3) target.moveTo(path[i].x, path[i].y);
      else target.lineTo(path[i].x, path[i].y);
    }
    target.strokeStyle = `rgb(${rgb.join(' ')} / ${alpha})`;
    target.lineWidth = width;
    target.stroke();
  }

  /** Cooled extrusion: a thin cyan line with a soft glow. */
  function bake(to: number) {
    if (!rctx || to <= baked) return;
    rctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    rctx.globalCompositeOperation = 'lighter';
    rctx.lineCap = 'butt';
    rctx.lineJoin = 'round';
    stroke(rctx, baked, to, CYAN, 0.07, 7);
    stroke(rctx, baked, to, CYAN, 0.38, 1.4);
    baked = to;
  }

  function burst(x: number, y: number, count: number, now: number) {
    for (let i = 0; i < count; i++) {
      const angle = Math.random() * Math.PI * 2;
      const speed = 180 + Math.random() * 420;
      sparks.push({ x, y, vx: Math.cos(angle) * speed, vy: Math.sin(angle) * speed - 120, born: now, life: 450 + Math.random() * 450 });
    }
  }

  function draw(now: number) {
    if (!ctx) return;
    const t = now - start;
    const extrudeMs = (length / SPEED) * 1000 * 1.06;
    if (t >= TRAVEL_MS) {
      // Stamp each sample with the moment the nozzle passed it: cooling runs on time, also after the head stops.
      const head = length * accelerate((t - TRAVEL_MS) / extrudeMs);
      while (printed < path.length && path[printed].s <= head) path[printed++].at = now;
    }
    let cool = baked;
    while (cool < printed && now - (path[cool].at ?? now) >= COOL_MS) cool++;
    bake(Math.min(cool, path.length - 1));

    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.drawImage(residual, 0, 0);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.globalCompositeOperation = 'lighter';
    ctx.lineCap = 'butt';
    ctx.lineJoin = 'round';

    // The travel move: fast, dashed, fading; then the nozzle lands with a burst.
    if (t < TRAVEL_MS + 700) {
      const u = Math.min(1, t / TRAVEL_MS);
      const e = u * u * (3 - 2 * u);
      const x = from.x + (path[0].x - from.x) * e;
      const y = from.y + (path[0].y - from.y) * e;
      ctx.setLineDash([3, 7]);
      ctx.beginPath();
      ctx.moveTo(from.x, from.y);
      ctx.lineTo(x, y);
      ctx.strokeStyle = `rgb(255 255 255 / ${0.35 * (1 - Math.max(0, t - TRAVEL_MS) / 700)})`;
      ctx.lineWidth = 1;
      ctx.stroke();
      ctx.setLineDash([]);
      if (t < TRAVEL_MS) {
        ctx.fillStyle = 'rgb(255 255 255 / 0.9)';
        ctx.beginPath();
        ctx.arc(x, y, 2.5, 0, Math.PI * 2);
        ctx.fill();
      } else if (corner === 0) {
        burst(path[0].x, path[0].y, 14, now);
        corner = 1;
      }
    }

    // Hot extrusion, in bands from cooling to white-hot.
    const bands = 32;
    for (let b = 0; b < bands; b++) {
      const lo = cool + Math.floor(((printed - 1 - cool) * b) / bands);
      const hi = cool + Math.floor(((printed - 1 - cool) * (b + 1)) / bands);
      if (hi <= lo) continue;
      const age = now - (path[Math.round((lo + hi) / 2)].at ?? now);
      const heat = 1 - Math.min(1, age / COOL_MS);
      const rgb = heatColor(age);
      stroke(ctx, lo, hi, rgb, 0.1 + 0.22 * heat, 8 + 6 * heat);
      stroke(ctx, lo, hi, rgb, 0.45 + 0.55 * heat, 1.4 + 1.2 * heat);
    }

    // Sparks fly at each corner the head passes.
    for (let i = Math.max(corner, 1); i < printed; i++)
      if (path[i].corner) {
        burst(path[i].x, path[i].y, 7, now);
        corner = i + 1;
      }

    const extruding = t >= TRAVEL_MS && printed < path.length;
    if (extruding) {
      const p = path[Math.max(0, printed - 1)];
      const glow = ctx.createRadialGradient(p.x, p.y, 0, p.x, p.y, 26);
      glow.addColorStop(0, 'rgb(255 255 255 / 0.95)');
      glow.addColorStop(0.18, 'rgb(255 214 150 / 0.55)');
      glow.addColorStop(1, 'rgb(71 191 255 / 0)');
      ctx.fillStyle = glow;
      ctx.beginPath();
      ctx.arc(p.x, p.y, 26, 0, Math.PI * 2);
      ctx.fill();
    }

    // Sparks: short streaks with gravity.
    const dt = 1 / 60;
    sparks = sparks.filter((spark) => now - spark.born < spark.life);
    for (const spark of sparks) {
      const life = (now - spark.born) / spark.life;
      spark.vy += 900 * dt;
      spark.x += spark.vx * dt;
      spark.y += spark.vy * dt;
      ctx.beginPath();
      ctx.moveTo(spark.x - spark.vx * 0.025, spark.y - spark.vy * 0.025);
      ctx.lineTo(spark.x, spark.y);
      ctx.strokeStyle = `rgb(${mix(WHITE, AMBER, life).join(' ')} / ${1 - life})`;
      ctx.lineWidth = 1.2;
      ctx.stroke();
    }

    // The second loop closes: one pulse of the whole frame.
    if (printed === path.length && closedAt < 0) {
      closedAt = now;
      const end = path[path.length - 1];
      burst(end.x, end.y, 18, now);
    }
    const pulse = closedAt < 0 ? -1 : (now - closedAt) / 900;
    if (pulse >= 0 && pulse < 1) {
      ctx.globalAlpha = 0.9 * Math.sin(Math.PI * pulse);
      ctx.setTransform(1, 0, 0, 1, 0, 0);
      ctx.drawImage(residual, 0, 0);
      ctx.globalAlpha = 1;
    }
    const busy = t < TRAVEL_MS + 700 || printed < path.length || baked < path.length - 1 || pulse < 1 || sparks.length > 0;
    raf = busy ? requestAnimationFrame(draw) : 0;
  }

  function finished() {
    layout();
    bake(path.length - 1);
    if (!ctx) return;
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.drawImage(residual, 0, 0);
  }

  function play() {
    cancelAnimationFrame(raf);
    if (reduced.matches) return finished();
    layout();
    rctx?.clearRect(0, 0, residual.width, residual.height);
    sparks = [];
    corner = 0;
    printed = 0;
    closedAt = -1;
    start = performance.now();
    raf = requestAnimationFrame(draw);
  }

  // A resize redraws the finished frame for the new size (mid-print, it skips to the end).
  let played = false;
  new ResizeObserver(() => {
    if (!played) return;
    cancelAnimationFrame(raf);
    raf = 0;
    finished();
  }).observe(frame);
  const seen = new IntersectionObserver(
    ([entry]) => {
      if (!entry.isIntersecting || played) return;
      played = true;
      seen.disconnect();
      setTimeout(play, Math.max(0, HEADLINE_MS - performance.now()));
    },
    { threshold: 0.15 },
  );
  seen.observe(frame);
  return {
    play() {
      played = true;
      play();
    },
  };
}
