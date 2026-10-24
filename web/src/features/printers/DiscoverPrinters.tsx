import { ChevronRightIcon, RadarIcon } from 'lucide-react';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { Spinner } from '@/components/ui/spinner';
import type { DiscoveredPrinterResponse } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsPrintersGetDiscoveredPrintersEndpoint } from '@/lib/api/generated/printers/printers';

interface DiscoverPrintersProps {
  onSelect: (printer: DiscoveredPrinterResponse) => void;
  onManual: () => void;
}

/** Live list of printers announcing themselves on the API host's LAN. */
export function DiscoverPrinters({
  onSelect,
  onManual,
}: DiscoverPrintersProps) {
  // Polling keeps the list live without a hub event: Bambu printers re-announce every few seconds,
  // and each read keeps the API's mDNS browse for Snapmaker printers going.
  const query = useOpenFarmApiEndpointsPrintersGetDiscoveredPrintersEndpoint({
    query: { refetchInterval: 3000 },
  });
  const data = query.data?.data;
  // Printers still to add come first; already-added ones sink to the bottom.
  const printers: DiscoveredPrinterResponse[] = Array.isArray(data)
    ? data.toSorted(
        (a, b) => Number(!!a.alreadyRegistered) - Number(!!b.alreadyRegistered),
      )
    : [];
  const manualLabel = 'Add by IP';

  if (query.isError) {
    return (
      <EmptyState
        title="Discovery failed"
        description="API did not respond."
        action={
          <div className="flex gap-2">
            <Button variant="outline" onClick={() => query.refetch()}>
              Retry
            </Button>
            <Button onClick={onManual}>{manualLabel}</Button>
          </div>
        }
      />
    );
  }

  if (!query.isLoading && printers.length === 0) {
    return (
      <EmptyState
        icon={<RadarIcon className="size-8 text-muted-foreground" />}
        title="No printers found"
        description="Check: powered on, same subnet, Bambu LAN mode on."
        action={<Button onClick={onManual}>{manualLabel}</Button>}
      />
    );
  }

  const newCount = printers.filter((p) => !p.alreadyRegistered).length;

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Spinner />
          Scanning LAN…
        </CardTitle>
        {!query.isLoading && (
          <CardDescription>
            {newCount === 0 ? 'All found printers added.' : `${newCount} new`}
          </CardDescription>
        )}
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <ul className="-mx-2 flex flex-col">
          {printers.map((printer) => {
            const summary = `${printer.brand} ${printer.modelCode ?? printer.rawModel} · ${printer.ipAddress}`;
            return (
              <li key={printer.serialNumber}>
                {printer.alreadyRegistered ? (
                  <div className="flex items-center justify-between gap-4 rounded-md px-2 py-3 text-muted-foreground">
                    <div className="min-w-0">
                      <p className="truncate font-medium">{printer.name}</p>
                      <p className="truncate text-sm">{summary}</p>
                    </div>
                    <Badge variant="secondary">Added</Badge>
                  </div>
                ) : (
                  <button
                    type="button"
                    aria-label={`Select ${printer.name}`}
                    onClick={() => onSelect(printer)}
                    className="flex w-full items-center justify-between gap-4 rounded-md px-2 py-3 text-left hover:bg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                  >
                    <div className="min-w-0">
                      <p className="truncate font-medium">{printer.name}</p>
                      <p className="truncate text-sm text-muted-foreground">
                        {summary}
                      </p>
                    </div>
                    <ChevronRightIcon className="size-4 shrink-0 text-muted-foreground" />
                  </button>
                )}
              </li>
            );
          })}
        </ul>
        <p className="text-sm text-muted-foreground">
          Not listed?{' '}
          <Button variant="link" className="h-auto p-0" onClick={onManual}>
            {manualLabel}
          </Button>
        </p>
      </CardContent>
    </Card>
  );
}
