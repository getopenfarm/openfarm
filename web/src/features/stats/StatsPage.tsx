import { DownloadIcon } from 'lucide-react';
import { useState } from 'react';
import { DataTableToolbar } from '@/components/data-table/DataTableToolbar';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { downloadFile } from '@/lib/api/downloadFile';
import { getOpenFarmApiEndpointsReportsExportMaterialsReportEndpointUrl } from '@/lib/api/generated/reports/reports';
import { MaterialsReportTable } from './components/MaterialsReportTable';
import {
  defaultRange,
  RangeFilter,
  rangeInstants,
} from './components/RangeFilter';

export function StatsPage() {
  const [range, setRange] = useState(defaultRange);
  const instants = rangeInstants(range);

  return (
    <Page>
      <PageHeader title="Stats" />
      <section className="flex flex-col gap-4">
        <PageHeader size="section" title="Filament usage" />
        <DataTableToolbar
          actions={
            <Button
              variant="outline"
              onClick={() =>
                downloadFile(
                  getOpenFarmApiEndpointsReportsExportMaterialsReportEndpointUrl(
                    instants,
                  ),
                  'materials-report.csv',
                  'Could not export CSV.',
                )
              }
            >
              <DownloadIcon data-icon="inline-start" />
              Export CSV
            </Button>
          }
        >
          <RangeFilter value={range} onChange={setRange} />
        </DataTableToolbar>
        <MaterialsReportTable range={instants} />
      </section>
    </Page>
  );
}
