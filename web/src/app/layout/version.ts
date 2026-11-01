// Injected by `define` in vite.config.ts (git describe, or OPENFARM_VERSION in Docker builds).
declare const __APP_VERSION__: string;

// The `v` is display only; git tags carry no prefix. `unknown` stays unprefixed.
export const APP_VERSION_LABEL =
  __APP_VERSION__ === 'unknown' ? __APP_VERSION__ : `v${__APP_VERSION__}`;
