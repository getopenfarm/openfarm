import { useQueryClient } from '@tanstack/react-query';
import { useContext, useEffect } from 'react';
import { getOpenFarmApiEndpointsActivityLogGetActivityLogEndpointQueryKey } from '@/lib/api/generated/activity-log/activity-log';
import type {
  FeedReadingResponse,
  FleetPrinterResponse,
  ListFleetResponse,
  PrinterDetailResponse,
  PrinterDetailTelemetryResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { getOpenFarmApiEndpointsFilamentsListFilamentShortagesEndpointQueryKey } from '@/lib/api/generated/filaments/filaments';
import { getOpenFarmApiEndpointsNotificationsListNotificationsEndpointQueryKey } from '@/lib/api/generated/notifications/notifications';
import {
  getOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpointQueryKey,
  getOpenFarmApiEndpointsPrintersGetPrinterDetailEndpointQueryKey,
  getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey,
} from '@/lib/api/generated/printers/printers';
import { getOpenFarmApiEndpointsReportsStatsEndpointQueryKey } from '@/lib/api/generated/reports/reports';
import { AccessContext } from '@/lib/auth/AccessContext';
import { invalidateFilamentTrackingQueries } from '@/lib/invalidateFilamentTrackingQueries';
import { connectFarmHub } from '@/lib/signalr/farmHubConnection';
import { rfidKey, topologyKey } from '../lib/feedSlotView';
import { filamentColorFromReadings } from '../lib/filamentColor';

interface PrinterStatusChangedPayload {
  printerId: string;
  status: string;
  isReachable: boolean;
}

interface PrinterTelemetryUpdatedPayload {
  printerId: string;
  telemetry: PrinterDetailTelemetryResponse;
  feedReadings: FeedReadingResponse[];
}

interface CameraFrameReceivedPayload {
  printerId: string;
  frameJpegBase64: string;
  receivedAtUtc: string;
}

export interface CameraFrameQueryData {
  frameJpegBase64: string;
  receivedAtUtc: number;
  fromSnapshot?: boolean;
}

/**
 * Shared latest-frame slot: live events and the snapshot bootstrap merge here by server receipt time.
 * The snapshot HTTP query uses a separate key so a slow response cannot replace a newer live frame.
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

/** The action-required list; refetched on a push and on every (re)connect, since missed pushes are never replayed. */
function invalidateNotificationQueries(
  queryClient: ReturnType<typeof useQueryClient>,
) {
  queryClient.invalidateQueries({
    queryKey:
      getOpenFarmApiEndpointsNotificationsListNotificationsEndpointQueryKey(),
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
  const access = useContext(AccessContext);
  const enabled = access === undefined || access.size > 0;

  useEffect(() => {
    if (!enabled) return;
    const topologyByPrinter = new Map<string, string>();
    const rfidByPrinter = new Map<string, string>();
    const nozzleByPrinter = new Map<string, string>();
    const setConnectionStatus = (status: FarmConnectionStatus) =>
      queryClient.setQueryData(farmConnectionQueryKey, status);
    setConnectionStatus('connecting');
    const { connection, stop } = connectFarmHub({
      onConnected: () => {
        setConnectionStatus('connected');
        invalidatePrinterQueries(queryClient);
        invalidateNotificationQueries(queryClient);
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
        invalidateNotificationQueries(queryClient);
      },
    });

    connection.on(
      'PrinterStatusChanged',
      (payload: PrinterStatusChangedPayload) => {
        // RFID items in Action required list only reachable printers.
        invalidateNotificationQueries(queryClient);
        // Stats split each printer's time by its status.
        queryClient.invalidateQueries({
          queryKey: getOpenFarmApiEndpointsReportsStatsEndpointQueryKey(),
        });
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
                  : {
                      ...current.data.telemetry,
                      nozzleConfirmed: false,
                      extruders: current.data.telemetry?.extruders?.map(
                        (extruder) => ({ ...extruder, nozzleConfirmed: false }),
                      ),
                    },
              },
            };
          },
        );
      },
    );

    connection.on(
      'PrinterTelemetryUpdated',
      (payload: PrinterTelemetryUpdatedPayload) => {
        // Every extruder's nozzle and confirmation: profile pickers and job matching depend on each.
        const extruders = payload.telemetry.extruders ?? [];
        const nozzle = JSON.stringify([
          payload.telemetry.nozzleDiameterMm ?? null,
          payload.telemetry.nozzleConfirmed ?? false,
          extruders.map((e) => [e.nozzleDiameterMm, e.nozzleConfirmed]),
        ]);
        const previousNozzle = nozzleByPrinter.get(payload.printerId);
        nozzleByPrinter.set(payload.printerId, nozzle);
        if (
          previousNozzle !== nozzle &&
          (previousNozzle !== undefined ||
            payload.telemetry.nozzleDiameterMm != null ||
            extruders.some((e) => e.nozzleDiameterMm != null))
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
        // Unregistered RFID spools in Action required are derived from the tags printers read.
        const rfid = rfidKey(payload.feedReadings);
        const previousRfid = rfidByPrinter.get(payload.printerId);
        rfidByPrinter.set(payload.printerId, rfid);
        if (previousRfid !== undefined && previousRfid !== rfid) {
          invalidateNotificationQueries(queryClient);
        }
        const topology = topologyKey(payload.feedReadings);
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
                feedReadings: payload.feedReadings,
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
                          filamentColorHex: filamentColorFromReadings(
                            payload.feedReadings,
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

    // Fires when a print closes, which is when automatic deduction changes spool weights.
    // Nothing else pushes those weights (SpoolStateChanged is never raised), so refresh them here.
    connection.on('NotificationsChanged', () => {
      invalidateNotificationQueries(queryClient);
      invalidateFilamentTrackingQueries(queryClient);
    });

    // Queued work moves the filament shortages shown on the catalog, wherever the operator is; a print starting or
    // ending moves the stats.
    connection.on('PrintJobStatusChanged', () => {
      queryClient.invalidateQueries({
        queryKey:
          getOpenFarmApiEndpointsFilamentsListFilamentShortagesEndpointQueryKey(),
      });
      queryClient.invalidateQueries({
        queryKey: getOpenFarmApiEndpointsReportsStatsEndpointQueryKey(),
      });
    });

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
        const receivedAtUtc = Date.parse(payload.receivedAtUtc);
        if (!Number.isFinite(receivedAtUtc)) return;
        queryClient.setQueryData<CameraFrameQueryData>(
          cameraFrameQueryKey(payload.printerId),
          (previous) =>
            previous && previous.receivedAtUtc > receivedAtUtc
              ? previous
              : { frameJpegBase64: payload.frameJpegBase64, receivedAtUtc },
        );
      },
    );

    return () => {
      stop();
    };
  }, [queryClient, enabled]);
}
