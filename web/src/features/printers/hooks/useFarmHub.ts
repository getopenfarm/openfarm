import { useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { getOpenFarmApiEndpointsActivityLogGetActivityLogEndpointQueryKey } from '@/lib/api/generated/activity-log/activity-log';
import type {
  AmsTrayResponse,
  AmsUnitResponse,
  FleetPrinterResponse,
  ListFleetResponse,
  PrinterDetailResponse,
  PrinterDetailTelemetryResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpointQueryKey,
  getOpenFarmApiEndpointsPrintersGetPrinterDetailEndpointQueryKey,
  getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey,
} from '@/lib/api/generated/printers/printers';
import { connectFarmHub } from '@/lib/signalr/farmHubConnection';
import { amsTopologyKey } from '../lib/feedSlotView';
import { filamentColorFromAms } from '../lib/filamentColor';

interface PrinterStatusChangedPayload {
  printerId: string;
  status: string;
  isReachable: boolean;
}

interface PrinterTelemetryUpdatedPayload {
  printerId: string;
  telemetry: PrinterDetailTelemetryResponse;
  amsUnits: AmsUnitResponse[];
  externalSpoolTray?: AmsTrayResponse | null;
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

export const farmConnectionQueryKey = ['farm-connection-status'] as const;
export type FarmConnectionStatus =
  | 'connecting'
  | 'connected'
  | 'reconnecting'
  | 'disconnected';

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
    const topologyByPrinter = new Map<string, string>();
    const nozzleByPrinter = new Map<string, string>();
    const setConnectionStatus = (status: FarmConnectionStatus) =>
      queryClient.setQueryData(farmConnectionQueryKey, status);
    setConnectionStatus('connecting');
    const { connection, stop } = connectFarmHub({
      onConnected: () => {
        setConnectionStatus('connected');
        invalidatePrinterQueries(queryClient);
      },
      onReconnecting: () => setConnectionStatus('reconnecting'),
      onClose: () => setConnectionStatus('disconnected'),
      onReconnected: () => {
        setConnectionStatus('connected');
        invalidateFilamentTrackingQueries(queryClient);
        topologyByPrinter.clear();
        queryClient.invalidateQueries({
          queryKey:
            getOpenFarmApiEndpointsActivityLogGetActivityLogEndpointQueryKey(),
        });
        nozzleByPrinter.clear();
        invalidatePrinterQueries(queryClient);
      },
    });

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
                          nozzleConfirmed: payload.isReachable
                            ? printer.nozzleConfirmed
                            : false,
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
                telemetry: payload.isReachable
                  ? current.data.telemetry
                  : { ...current.data.telemetry, nozzleConfirmed: false },
              },
            };
          },
        );
      },
    );

    connection.on(
      'PrinterTelemetryUpdated',
      (payload: PrinterTelemetryUpdatedPayload) => {
        const nozzle = `${payload.telemetry.nozzleDiameterMm ?? ''}:${payload.telemetry.nozzleConfirmed ?? false}`;
        const previousNozzle = nozzleByPrinter.get(payload.printerId);
        nozzleByPrinter.set(payload.printerId, nozzle);
        if (
          previousNozzle !== nozzle &&
          (previousNozzle !== undefined ||
            payload.telemetry.nozzleDiameterMm != null)
        ) {
          queryClient.invalidateQueries({
            predicate: (query) =>
              query.queryKey.some(
                (key) =>
                  typeof key === 'string' &&
                  /profile-assignment-fleet|profiles\/machine|print-jobs|printers/.test(
                    key,
                  ),
              ),
          });
        }
        const topology = `${amsTopologyKey(payload.amsUnits)}:${payload.externalSpoolTray == null ? 'no-external' : 'external'}`;
        if (topologyByPrinter.get(payload.printerId) !== topology) {
          topologyByPrinter.set(payload.printerId, topology);
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpointQueryKey(
                payload.printerId,
              ),
          });
        }
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
                externalSpoolTray: payload.externalSpoolTray,
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
                          nozzleDiameterMm: payload.telemetry.nozzleDiameterMm,
                          nozzleObservedAtUtc:
                            payload.telemetry.nozzleObservedAtUtc,
                          nozzleConfirmed: payload.telemetry.nozzleConfirmed,
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

    connection.on(
      'PrinterActivityChanged',
      (payload: { printerId: string }) => {
        queryClient.invalidateQueries({
          queryKey:
            getOpenFarmApiEndpointsActivityLogGetActivityLogEndpointQueryKey(),
          predicate: (query) => {
            const filters = query.queryKey[1] as
              | { entityType?: string; entityId?: string }
              | undefined;
            return (
              (!filters?.entityType || filters.entityType === 'Printer') &&
              (!filters?.entityId || filters.entityId === payload.printerId)
            );
          },
        });
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

    return () => {
      stop();
    };
  }, [queryClient]);
}
