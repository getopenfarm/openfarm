import {
  BoxIcon,
  ClipboardListIcon,
  ComponentIcon,
  HistoryIcon,
  LayersIcon,
  LayoutDashboardIcon,
  type LucideIcon,
  PrinterIcon,
  SettingsIcon,
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

export const navSections: NavSection[] = [
  {
    label: 'Overview',
    items: [{ title: 'Dashboard', url: '/', icon: LayoutDashboardIcon }],
  },
  {
    label: 'Fleet',
    items: [{ title: 'Printers', url: '/printers', icon: PrinterIcon }],
  },
  {
    label: 'Production',
    items: [{ title: 'Jobs', url: '/jobs', icon: ClipboardListIcon }],
  },
  {
    label: 'Materials',
    items: [{ title: 'Filament', url: '/filament', icon: LayersIcon }],
  },
  {
    label: 'Library',
    items: [
      { title: 'Parts', url: '/parts', icon: BoxIcon },
      { title: 'Assemblies', url: '/assemblies', icon: ComponentIcon },
    ],
  },
  {
    label: 'Maintenance',
    items: [{ title: 'Maintenance', url: '/maintenance', icon: WrenchIcon }],
  },
  {
    label: 'System',
    items: [
      { title: 'Activity Log', url: '/activity-log', icon: HistoryIcon },
      { title: 'Settings', url: '/settings', icon: SettingsIcon },
    ],
  },
];
