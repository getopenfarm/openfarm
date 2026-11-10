import type { SidebarsConfig } from '@docusaurus/plugin-content-docs';

// Keep the reading path deliberate: first success, concepts, setup, daily work,
// administration, and recovery. File names do not decide the navigation order.
const sidebars: SidebarsConfig = {
  guide: [
    'quick-start',
    {
      type: 'category',
      label: 'Overview',
      collapsed: false,
      items: ['overview/openfarm', 'overview/concepts'],
    },
    {
      type: 'category',
      label: 'Installation',
      items: [
        'installation/docker',
        'installation/configuration',
        'installation/upgrades-backups',
      ],
    },
    {
      type: 'category',
      label: 'Connecting printers',
      items: [
        'printers/supported-printers',
        'printers/bambu',
        'printers/snapmaker',
        'printers/manage-fleet',
      ],
    },
    {
      type: 'category',
      label: 'Filament & spools',
      items: ['filament/catalog', 'filament/spools', 'filament/feed-units'],
    },
    {
      type: 'category',
      label: 'Parts & profiles',
      items: [
        'library/parts',
        'library/profiles',
        'library/profile-behavior',
        'library/assemblies',
      ],
    },
    {
      type: 'category',
      label: 'Production',
      items: [
        'production/queue',
        'production/runs',
        'production/matching',
        'production/backfill',
        'production/outcomes',
      ],
    },
    {
      type: 'category',
      label: 'Farm operations',
      items: [
        'operations/dashboard',
        'operations/maintenance',
        'operations/stats',
      ],
    },
    {
      type: 'category',
      label: 'Costs & pricing',
      items: ['costs/catalog', 'costs/pricing'],
    },
    {
      type: 'category',
      label: 'Administration',
      items: [
        'administration/accounts',
        'administration/ai-assistants',
        'administration/settings',
      ],
    },
    {
      type: 'category',
      label: 'API & development',
      items: ['development/api', 'development/local-setup'],
    },
    'troubleshooting',
    'support',
  ],
};

export default sidebars;
