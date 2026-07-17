export type BadgeKind =
  | 'zinc'
  | 'green'
  | 'amber'
  | 'orange'
  | 'red'
  | 'violet'
  | 'blue';

interface KindMeta {
  dotClass: string;
  textClass: string;
  bgClass: string;
  /** Soft colored ring for a dot glow (pair with `ring-4`). */
  ringClass: string;
}

export const BADGE_KIND_META: Record<BadgeKind, KindMeta> = {
  zinc: {
    dotClass: 'bg-kind-zinc',
    textClass: 'text-kind-zinc',
    bgClass: 'bg-kind-zinc/10',
    ringClass: 'ring-kind-zinc/15',
  },
  green: {
    dotClass: 'bg-kind-green',
    textClass: 'text-kind-green',
    bgClass: 'bg-kind-green/10',
    ringClass: 'ring-kind-green/15',
  },
  amber: {
    dotClass: 'bg-kind-amber',
    textClass: 'text-kind-amber',
    bgClass: 'bg-kind-amber/10',
    ringClass: 'ring-kind-amber/15',
  },
  orange: {
    dotClass: 'bg-kind-orange',
    textClass: 'text-kind-orange',
    bgClass: 'bg-kind-orange/10',
    ringClass: 'ring-kind-orange/15',
  },
  red: {
    dotClass: 'bg-kind-red',
    textClass: 'text-kind-red',
    bgClass: 'bg-kind-red/10',
    ringClass: 'ring-kind-red/15',
  },
  violet: {
    dotClass: 'bg-kind-violet',
    textClass: 'text-kind-violet',
    bgClass: 'bg-kind-violet/10',
    ringClass: 'ring-kind-violet/15',
  },
  blue: {
    dotClass: 'bg-kind-blue',
    textClass: 'text-kind-blue',
    bgClass: 'bg-kind-blue/10',
    ringClass: 'ring-kind-blue/15',
  },
};
