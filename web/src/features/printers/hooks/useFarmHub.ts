import * as signalR from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import type {
  FleetPrinterResponse,
  ListFleetResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey } from '@/lib/api/generated/printers/printers';
import { getToken } from '@/lib/auth/tokenStorage';

const API_BASE_URL =
  import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:8888';

interface PrinterStatusChangedPayload {
  printerId: string;
  status: string;
  isReachable: boolean;
}

type FleetQueryData = {
  data: ListFleetResponse;
  status: number;
  headers: Headers;
};

/**
 * Opens the /hubs/farm SignalR connection and writes incoming events directly into TanStack
 * Query's cache (contracts/farm-hub.md) — the sole real-time entry point, no polling fallback.
 * This phase wires PrinterStatusChanged into the fleet-list query; Phase 4/5 extend this same
 * hook for PrinterTelemetryUpdated/CameraFrameReceived once a detail-page consumer exists.
 */
export function useFarmHub() {
  const queryClient = useQueryClient();

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${API_BASE_URL}/hubs/farm`, {
        accessTokenFactory: () => getToken() ?? '',
        // Auth is via the JWT bearer token above, not cookies — disabling credentialed
        // requests avoids requiring Access-Control-Allow-Credentials on the CORS policy.
        withCredentials: false,
      })
      .withAutomaticReconnect()
      .build();

    connection.on(
      'PrinterStatusChanged',
      (payload: PrinterStatusChangedPayload) => {
        const queryKey =
          getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey();
        queryClient.setQueryData<FleetQueryData | undefined>(
          queryKey,
          (current) => {
            if (!current?.data.printers) {
              return current;
            }

            return {
              ...current,
              data: {
                printers: current.data.printers.map(
                  (printer: FleetPrinterResponse) =>
                    printer.id === payload.printerId
                      ? {
                          ...printer,
                          status: payload.status,
                          isReachable: payload.isReachable,
                        }
                      : printer,
                ),
              },
            };
          },
        );
      },
    );

    connection.start().catch(() => {
      // withAutomaticReconnect() handles retries; a failed initial connect just means no
      // live updates until connectivity returns.
    });

    return () => {
      connection.stop();
    };
  }, [queryClient]);
}
