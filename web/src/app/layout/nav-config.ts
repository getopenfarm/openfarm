import {
  BoxIcon,
  ChartColumnIcon,
  ClipboardListIcon,
  ComponentIcon,
  HistoryIcon,
  LayersIcon,
  LayoutDashboardIcon,
  ListChecksIcon,
  type LucideIcon,
  PackageIcon,
  PrinterIcon,
  SlidersHorizontalIcon,
  UsersIcon,
  WrenchIcon,
} from 'lucide-react';

export interface NavItem {
  title: string;
  url: string;
  icon: LucideIcon;
}

export interface NavSection {
  label: string;
  items: NavItem[];
}

const dashboard: NavItem = {
  title: 'Dashboard',
  url: '/',
  icon: LayoutDashboardIcon,
};
const printers: NavItem = {
  title: 'Printers',
  url: '/printers',
  icon: PrinterIcon,
};
const runs: NavItem = {
  title: 'Runs',
  url: '/production-runs',
  icon: ListChecksIcon,
};
const jobs: NavItem = { title: 'Queue', url: '/jobs', icon: ClipboardListIcon };
const stats: NavItem = { title: 'Stats', url: '/stats', icon: ChartColumnIcon };
const filament: NavItem = {
  title: 'Filament',
  url: '/filament',
  icon: LayersIcon,
};
const parts: NavItem = { title: 'Parts', url: '/parts', icon: BoxIcon };
const assemblies: NavItem = {
  title: 'Assemblies',
  url: '/assemblies',
  icon: ComponentIcon,
};
const nonPrintedComponents: NavItem = {
  title: 'Cost catalog',
  url: '/cost-catalog',
  icon: PackageIcon,
};
const profiles: NavItem = {
  title: 'Profiles',
  url: '/profiles',
  icon: SlidersHorizontalIcon,
};
const maintenance: NavItem = {
  title: 'Maintenance',
  url: '/maintenance',
  icon: WrenchIcon,
};
const activityLog: NavItem = {
  title: 'Activity log',
  url: '/activity-log',
  icon: HistoryIcon,
};

export const navSections: NavSection[] = [
  { label: 'Operate', items: [dashboard, printers, jobs, runs, stats] },
  {
    label: 'Library',
    items: [
      filament,
      parts,
      assemblies,
      nonPrintedComponents,
      profiles,
      maintenance,
    ],
  },
];

export const adminNavSection: NavSection = {
  label: 'Administration',
  items: [
    { title: 'Users & Groups', url: '/users-groups', icon: UsersIcon },
    activityLog,
  ],
};
