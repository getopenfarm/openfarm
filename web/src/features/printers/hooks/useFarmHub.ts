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
import { filamentColorFromAms } from '../lib/filamentColor';

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

interface CameraFrameReceivedPayload {
  printerId: string;
  frameJpegBase64: string;
}

export interface CameraFrameQueryData {
  frameJpegBase64: string;
  receivedAtUtc: number;
}

/**
 * Not backed by any HTTP endpoint — CameraFrameReceived is push-only (contracts/farm-hub.md), so
 * this cache slot exists purely as a place for useFarmHub to write frames and CameraView to read
 * them reactively, same TanStack Query cache used for every other real-time update in this app.
 */
export function cameraFrameQueryKey(printerId: string) {
  return ['printer-camera-frame', printerId] as const;
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
 * Refetches every fleet-list/printer-detail query in the cache, regardless of which printer id
 * each detail key carries. Called after the SignalR connection (re)connects so a dropped
 * connection can't leave the UI stuck on stale status/reachability forever — events that fired
 * while disconnected are never replayed by SignalR itself, so this is the catch-up.
 */
function invalidatePrinterQueries(
  queryClient: ReturnType<typeof useQueryClient>,
) {
  queryClient.invalidateQueries({
    predicate: (query) =>
      typeof query.queryKey[0] === 'string' &&
      query.queryKey[0].startsWith('/api/v1/printers'),
  });
}

/** Matches both the printer-scoped feed-units list (/api/v1/printers/{id}/feed-units) and the
 * global spools list (/api/v1/spools) — spool state and Feed Unit topology are shown together
 * on FeedUnitsCard/SpoolsTable, so either changing invalidates both. */
function invalidateFilamentTrackingQueries(
  queryClient: ReturnType<typeof useQueryClient>,
) {
  queryClient.invalidateQueries({
    predicate: (query) =>
      typeof query.queryKey[0] === 'string' &&
      (query.queryKey[0].includes('/feed-units') ||
        query.queryKey[0].startsWith('/api/v1/spools')),
  });
}

/**
 * Opens the /hubs/farm SignalR connection and writes incoming events directly into TanStack
 * Query's cache (contracts/farm-hub.md) — the sole real-time entry point, no polling fallback.
 * Wires PrinterStatusChanged into the fleet-list query, both PrinterStatusChanged and
 * PrinterTelemetryUpdated into the printer-detail query when that printer's detail is cached, and
 * CameraFrameReceived into the dedicated cameraFrameQueryKey cache slot CameraView reads from.
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
                          nozzleTempC: payload.telemetry.nozzleTempC,
                          bedTempC: payload.telemetry.bedTempC,
                          printProgressPercent:
                            payload.telemetry.printProgressPercent,
                          currentStageLabel:
                            payload.telemetry.currentStageLabel,
                          remainingTimeMinutes:
                            payload.telemetry.remainingTimeMinutes,
                          currentJobName: payload.telemetry.currentJobName,
                          filamentColorHex: filamentColorFromAms(
                            payload.amsUnits,
                          ),
                        }
                      : printer,
                ),
              },
            };
          },
        );
      },
    );

    connection.on('SpoolStateChanged', () => {
      invalidateFilamentTrackingQueries(queryClient);
    });

    connection.on('FeedUnitAutoContinued', () => {
      invalidateFilamentTrackingQueries(queryClient);
    });

    connection.on('FeedUnitsChanged', () => {
      invalidateFilamentTrackingQueries(queryClient);
    });

    connection.on(
      'CameraFrameReceived',
      (payload: CameraFrameReceivedPayload) => {
        queryClient.setQueryData<CameraFrameQueryData>(
          cameraFrameQueryKey(payload.printerId),
          {
            frameJpegBase64: payload.frameJpegBase64,
            receivedAtUtc: Date.now(),
          },
        );
      },
    );

    connection.onreconnecting((error) => {
      console.warn(
        '[useFarmHub] SignalR connection lost, reconnecting…',
        error,
      );
    });

    connection.onreconnected(() => {
      console.info(
        '[useFarmHub] SignalR reconnected — refetching printer data to catch up on any updates missed while disconnected.',
      );
      invalidatePrinterQueries(queryClient);
    });

    connection.onclose((error) => {
      console.warn(
        '[useFarmHub] SignalR connection closed — no more live updates until this component remounts.',
        error,
      );
    });

    const startPromise = connection
      .start()
      .then(() => {
        console.info('[useFarmHub] SignalR connected.');
        invalidatePrinterQueries(queryClient);
      })
      .catch((error: unknown) => {
        // withAutomaticReconnect() handles retries; a failed initial connect just means no
        // live updates until connectivity returns.
        console.warn('[useFarmHub] SignalR failed to connect.', error);
      });

    return () => {
      // Calling stop() while start() is still negotiating throws ("connection was stopped
      // during negotiation") and logs a console error — happens on every mount under React
      // StrictMode's dev-only double-invoke. Wait for start() to settle first so stop() always
      // acts on a connection that's actually in a stoppable state.
      startPromise.finally(() => connection.stop());
    };
  }, [queryClient]);
}
