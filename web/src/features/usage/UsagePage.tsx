import { DownloadIcon } from 'lucide-react';
import { useState } from 'react';
import {
  DataTableTabsList,
  DataTableToolbar,
} from '@/components/data-table/DataTableToolbar';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Swatch } from '@/components/swatch/Swatch';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Tabs, TabsContent, TabsTrigger } from '@/components/ui/tabs';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import { useFilamentCatalog } from '@/features/filament/data/useFilamentCatalog';
import { downloadFile } from '@/lib/api/downloadFile';
import { getOpenFarmApiEndpointsReportsExportMaterialsReportEndpointUrl } from '@/lib/api/generated/reports/reports';
import { getOpenFarmApiEndpointsSpoolsExportSpoolHistoryEndpointUrl } from '@/lib/api/generated/spools/spools';
import { MaterialsReportTable } from './components/MaterialsReportTable';
import { SpoolHistoryTable } from './components/SpoolHistoryTable';
import { lastDays, toInstants } from './lib/range';

const ALL = 'all';

const SOURCES = [
  { value: 'Job', label: 'Print' },
  { value: 'Manual', label: 'Manual usage' },
  { value: 'Correction', label: 'Weight correction' },
  { value: 'WriteOff', label: 'Finished' },
  { value: 'Restore', label: 'Restored' },
];

export function UsagePage() {
  const filaments = useFilamentCatalog();
  const [tab, setTab] = useState('materials');
  const [preset, setPreset] = useState('30');
  const [custom, setCustom] = useState(() => lastDays(30));
  const [variantId, setVariantId] = useState(ALL);
  const [source, setSource] = useState(ALL);

  const range = toInstants(
    preset === 'custom' ? custom : lastDays(Number(preset)),
  );
  const filter = {
    ...range,
    ...(variantId === ALL ? {} : { filamentVariantId: variantId }),
    ...(source === ALL ? {} : { source }),
  };

  return (
    <Page>
      <PageHeader
        title="Usage"
        description="Filament used, wasted and written off."
        action={
          <Button
            variant="outline"
            onClick={() =>
              tab === 'materials'
                ? downloadFile(
                    getOpenFarmApiEndpointsReportsExportMaterialsReportEndpointUrl(
                      range,
                    ),
                    'materials-report.csv',
                    'Could not export CSV.',
                  )
                : downloadFile(
                    getOpenFarmApiEndpointsSpoolsExportSpoolHistoryEndpointUrl(
                      filter,
                    ),
                    'spool-history.csv',
                    'Could not export CSV.',
                  )
            }
          >
            <DownloadIcon data-icon="inline-start" />
            Export CSV
          </Button>
        }
      />
      <Tabs value={tab} onValueChange={setTab} className="gap-4">
        <DataTableToolbar>
          <DataTableTabsList aria-label="Usage view">
            <TabsTrigger value="materials">By material</TabsTrigger>
            <TabsTrigger value="history">History</TabsTrigger>
          </DataTableTabsList>
          <ToggleGroup
            aria-label="Range"
            type="single"
            variant="outline"
            value={preset}
            onValueChange={(next) => {
              if (next) setPreset(next);
            }}
          >
            <ToggleGroupItem value="7">7 d</ToggleGroupItem>
            <ToggleGroupItem value="30">30 d</ToggleGroupItem>
            <ToggleGroupItem value="90">90 d</ToggleGroupItem>
            <ToggleGroupItem value="custom">Custom</ToggleGroupItem>
          </ToggleGroup>
          {preset === 'custom' && (
            <div className="flex items-center gap-2">
              <Input
                type="date"
                aria-label="From"
                className="w-auto"
                value={custom.start}
                max={custom.end}
                onChange={(event) => {
                  const start = event.target.value;
                  if (start) setCustom((span) => ({ ...span, start }));
                }}
              />
              <span className="text-muted-foreground">–</span>
              <Input
                type="date"
                aria-label="To"
                className="w-auto"
                value={custom.end}
                min={custom.start}
                onChange={(event) => {
                  const end = event.target.value;
                  if (end) setCustom((span) => ({ ...span, end }));
                }}
              />
            </div>
          )}
          {tab === 'history' && (
            <>
              <Select value={variantId} onValueChange={setVariantId}>
                <SelectTrigger aria-label="Color" className="w-60">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={ALL}>All colors</SelectItem>
                  {filaments.flatMap((filament) =>
                    (filament.variants ?? []).map((variant) => (
                      <SelectItem key={variant.id} value={variant.id ?? ''}>
                        <Swatch
                          colorHex={variant.colorHex ?? undefined}
                          size="sm"
                        />
                        {filament.vendorName} {filament.material} ·{' '}
                        {variant.colorName}
                      </SelectItem>
                    )),
                  )}
                </SelectContent>
              </Select>
              <Select value={source} onValueChange={setSource}>
                <SelectTrigger aria-label="Source" className="w-44">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={ALL}>All sources</SelectItem>
                  {SOURCES.map((option) => (
                    <SelectItem key={option.value} value={option.value}>
                      {option.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </>
          )}
        </DataTableToolbar>
        <TabsContent value="materials">
          <MaterialsReportTable range={range} />
        </TabsContent>
        <TabsContent value="history">
          <SpoolHistoryTable filter={filter} />
        </TabsContent>
      </Tabs>
    </Page>
  );
}
