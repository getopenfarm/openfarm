import type { Config } from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';
import { themes as prismThemes } from 'prism-react-renderer';

const config: Config = {
  title: 'OpenFarm docs',
  tagline: 'Set up your farm. Keep production moving.',
  favicon: 'img/openfarm.svg',
  url: process.env.DOCS_URL ?? 'https://getopenfarm.github.io',
  baseUrl: process.env.DOCS_BASE_URL ?? '/docs/',
  organizationName: 'getopenfarm',
  projectName: 'getopenfarm.github.io',
  trailingSlash: true,
  onBrokenLinks: 'throw',
  onBrokenAnchors: 'throw',
  markdown: {
    hooks: { onBrokenMarkdownLinks: 'throw' },
  },
  i18n: { defaultLocale: 'en', locales: ['en'] },
  presets: [
    [
      'classic',
      {
        docs: {
          path: 'content',
          routeBasePath: '/',
          sidebarPath: './sidebars.ts',
          editUrl: 'https://github.com/getopenfarm/openfarm/edit/main/docs/',
          showLastUpdateTime: false,
        },
        blog: false,
        theme: { customCss: './src/css/custom.css' },
      } satisfies Preset.Options,
    ],
  ],
  themes: [
    [
      '@easyops-cn/docusaurus-search-local',
      {
        hashed: true,
        indexDocs: true,
        indexBlog: false,
        indexPages: false,
        docsDir: 'content',
        docsRouteBasePath: '/',
        language: 'en',
        highlightSearchTermsOnTargetPage: true,
      },
    ],
  ],
  themeConfig: {
    image: 'img/openfarm.svg',
    colorMode: { defaultMode: 'light', respectPrefersColorScheme: true },
    navbar: {
      title: 'OpenFarm',
      logo: { alt: '', src: 'img/openfarm.svg' },
      items: [
        {
          href: 'https://getopenfarm.github.io/',
          label: 'Website',
          position: 'left',
        },
        {
          type: 'docSidebar',
          sidebarId: 'guide',
          label: 'Documentation',
          position: 'left',
        },
        {
          href: 'https://github.com/getopenfarm/openfarm',
          label: 'GitHub',
          position: 'right',
        },
      ],
    },
    docs: { sidebar: { hideable: false, autoCollapseCategories: false } },
    tableOfContents: { minHeadingLevel: 2, maxHeadingLevel: 3 },
    footer: {
      style: 'light',
      links: [
        {
          title: 'OpenFarm',
          items: [
            { label: 'Documentation', to: '/' },
            {
              label: 'Source code',
              href: 'https://github.com/getopenfarm/openfarm',
            },
            {
              label: 'Report an issue',
              href: 'https://github.com/getopenfarm/openfarm/issues',
            },
          ],
        },
      ],
      copyright: 'OpenFarm · Open source print farm management',
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.vsDark,
      additionalLanguages: ['bash', 'json', 'csharp', 'yaml'],
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
