import * as signalR from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import type {
  AmsUnitResponse,
  FleetPrinterResponse,
  ListFleetResponse,
  PrinterDetailResponse,
  PrinterDetailTelemetryResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsPrintersGetPrinterDetailEndpointQueryKey,
  getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey,
} from '@/lib/api/generated/printers/printers';
import { getToken } from '@/lib/auth/tokenStorage';

const API_BASE_URL =
  import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:8888';

interface PrinterStatusChangedPayload {
  printerId: string;
  status: string;
  isReachable: boolean;
}

interface PrinterTelemetryUpdatedPayload {
  printerId: string;
  telemetry: PrinterDetailTelemetryResponse;
  amsUnits: AmsUnitResponse[];
}

type FleetQueryData = {
  data: ListFleetResponse;
  status: number;
  headers: Headers;
};
type DetailQueryData = {
  data: PrinterDetailResponse;
  status: number;
  headers: Headers;
};

/**
 * Opens the /hubs/farm SignalR connection and writes incoming events directly into TanStack
 * Query's cache (contracts/farm-hub.md) — the sole real-time entry point, no polling fallback.
 * Wires PrinterStatusChanged into the fleet-list query, and both PrinterStatusChanged and
 * PrinterTelemetryUpdated into the printer-detail query when that printer's detail is cached.
 * Phase 5 extends this hook further for CameraFrameReceived.
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
        queryClient.setQueryData<FleetQueryData | undefined>(
          getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey(),
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

        queryClient.setQueryData<DetailQueryData | undefined>(
          getOpenFarmApiEndpointsPrintersGetPrinterDetailEndpointQueryKey(
            payload.printerId,
          ),
          (current) => {
            if (!current?.data || current.data.id !== payload.printerId) {
              return current;
            }

            return {
              ...current,
              data: {
                ...current.data,
                status: payload.status,
                isReachable: payload.isReachable,
              },
            };
          },
        );
      },
    );

    connection.on(
      'PrinterTelemetryUpdated',
      (payload: PrinterTelemetryUpdatedPayload) => {
        queryClient.setQueryData<DetailQueryData | undefined>(
          getOpenFarmApiEndpointsPrintersGetPrinterDetailEndpointQueryKey(
            payload.printerId,
          ),
          (current) => {
            if (!current?.data || current.data.id !== payload.printerId) {
              return current;
            }

            return {
              ...current,
              data: {
                ...current.data,
                telemetry: payload.telemetry,
                amsUnits: payload.amsUnits,
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
