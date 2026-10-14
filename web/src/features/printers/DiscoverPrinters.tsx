import { RadarIcon } from 'lucide-react';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
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
  // Printers re-announce every few seconds; polling keeps the list live without a hub event.
  const query = useOpenFarmApiEndpointsPrintersGetDiscoveredPrintersEndpoint({
    query: { refetchInterval: 3000 },
  });
  const data = query.data?.data;
  const printers: DiscoveredPrinterResponse[] = Array.isArray(data) ? data : [];

  if (query.isError) {
    return (
      <EmptyState
        title="Couldn't scan for printers"
        description="The server didn't respond to the discovery request."
        action={
          <div className="flex gap-2">
            <Button variant="outline" onClick={() => query.refetch()}>
              Retry
            </Button>
            <Button onClick={onManual}>Enter manually</Button>
          </div>
        }
      />
    );
  }

  if (!query.isLoading && printers.length === 0) {
    return (
      <EmptyState
        icon={<RadarIcon className="size-8 text-muted-foreground" />}
        title="No printers found yet"
        description="Make sure the printer is powered on, on the same network as OpenFarm, and has LAN mode enabled. This list updates automatically."
        action={<Button onClick={onManual}>Enter manually</Button>}
      />
    );
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Spinner />
          Scanning your network…
        </CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <ul className="flex flex-col divide-y">
          {printers.map((printer) => (
            <li
              key={printer.serialNumber}
              className="flex items-center justify-between gap-4 py-3"
            >
              <div className="min-w-0">
                <p className="truncate font-medium">{printer.name}</p>
                <p className="truncate text-sm text-muted-foreground">
                  {printer.modelCode ?? printer.rawModel} · {printer.ipAddress}{' '}
                  · {printer.serialNumber}
                </p>
              </div>
              {printer.alreadyRegistered ? (
                <Badge variant="secondary">Already added</Badge>
              ) : (
                <Button
                  size="sm"
                  aria-label={`Select ${printer.name}`}
                  onClick={() => onSelect(printer)}
                >
                  Select
                </Button>
              )}
            </li>
          ))}
        </ul>
        <Button variant="link" className="self-start px-0" onClick={onManual}>
          Don't see it? Enter manually →
        </Button>
      </CardContent>
    </Card>
  );
}
