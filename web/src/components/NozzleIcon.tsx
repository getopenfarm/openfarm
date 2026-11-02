import type { SVGProps } from 'react';

/** Hot end laying a filament line — Lucide-style (24px grid, 2px stroke). */
export function NozzleIcon(props: SVGProps<SVGSVGElement>) {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      width="24"
      height="24"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      {...props}
    >
      <rect x="6" y="2" width="12" height="7" rx="1" />
      <path d="M9 9l2 4h2l2-4" />
      <path d="M12 13v4a3 3 0 0 1-3 3H3" />
    </svg>
  );
}
