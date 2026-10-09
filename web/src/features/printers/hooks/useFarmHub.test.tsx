import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  getOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpointQueryKey,
  getOpenFarmApiEndpointsPrintersGetPrinterDetailEndpointQueryKey,
} from '@/lib/api/generated/printers/printers';
import { useFarmHub } from './useFarmHub';

const hub = vi.hoisted(() => ({
  handlers: new Map<string, (payload: unknown) => void>(),
  reconnected: () => {},
  stop: vi.fn(),
}));
vi.mock('@/lib/signalr/farmHubConnection', () => ({
  connectFarmHub: (options: { onReconnected: () => void }) => {
    hub.reconnected = options.onReconnected;
    return {
      connection: {
        on: (event: string, handler: (payload: unknown) => void) =>
          hub.handlers.set(event, handler),
      },
      stop: hub.stop,
    };
  },
}));

describe('Printer activity live updates', () => {
  beforeEach(() => hub.handlers.clear());
  it('refreshes this printer and organization history while retaining other printer caches', () => {
    const client = new QueryClient();
    const own = [
      '/api/v1/activity-log',
      { page: 1, pageSize: 20, entityType: 'Printer', entityId: 'p1' },
    ];
    const other = [
      '/api/v1/activity-log',
      { page: 1, pageSize: 20, entityType: 'Printer', entityId: 'p2' },
    ];
    const global = ['/api/v1/activity-log', { page: 1, pageSize: 25 }];
    for (const key of [own, other, global])
      client.setQueryData(key, { items: [] });
    const { unmount } = renderHook(useFarmHub, {
      wrapper: ({ children }: { children: ReactNode }) => (
        <QueryClientProvider client={client}>{children}</QueryClientProvider>
      ),
    });
    act(() =>
      hub.handlers.get('PrinterActivityChanged')?.({ printerId: 'p1' }),
    );
    expect(client.getQueryState(own)?.isInvalidated).toBe(true);
    expect(client.getQueryState(global)?.isInvalidated).toBe(true);
    expect(client.getQueryState(other)?.isInvalidated).toBe(false);
    act(() => hub.reconnected());
    expect(client.getQueryState(other)?.isInvalidated).toBe(true);
    unmount();
  });
});

describe('Feed Units live updates', () => {
  beforeEach(() => hub.handlers.clear());
  it('refreshes only the affected topology on first event and position changes while updating live contents', () => {
    const client = new QueryClient();
    const invalidate = vi.spyOn(client, 'invalidateQueries');
    const key =
      getOpenFarmApiEndpointsPrintersGetPrinterDetailEndpointQueryKey('p1');
    client.setQueryData(key, { data: { id: 'p1', amsUnits: [] } });
    const { unmount } = renderHook(useFarmHub, {
      wrapper: ({ children }: { children: ReactNode }) => (
        <QueryClientProvider client={client}>{children}</QueryClientProvider>
      ),
    });
    const emit = (printerId: string, percent: number, slotIndex = 2) =>
      act(() =>
        hub.handlers.get('PrinterTelemetryUpdated')?.({
          printerId,
          telemetry: {},
          amsUnits: [
            {
              unitIndex: 3,
              trays: [{ slotIndex, remainPercent: percent, active: true }],
            },
          ],
        }),
      );
    emit('p1', 62);
    expect(invalidate).toHaveBeenCalledWith({
      queryKey:
        getOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpointQueryKey('p1'),
    });
    invalidate.mockClear();
    emit('p1', 61);
    expect(invalidate).not.toHaveBeenCalled();
    expect(client.getQueryData(key)).toMatchObject({
      data: { amsUnits: [{ trays: [{ remainPercent: 61 }] }] },
    });
    emit('p1', 61, 7);
    expect(invalidate).toHaveBeenCalledTimes(1);
    emit('p2', 40);
    expect(invalidate).toHaveBeenLastCalledWith({
      queryKey:
        getOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpointQueryKey('p2'),
    });
    act(() => hub.reconnected());
    invalidate.mockClear();
    emit('p1', 60, 7);
    expect(invalidate).toHaveBeenCalledTimes(1);
    unmount();
    expect(hub.stop).toHaveBeenCalled();
  });
  it('updates external-port readings live without refetching for percentage changes', () => {
    const client = new QueryClient();
    const key =
      getOpenFarmApiEndpointsPrintersGetPrinterDetailEndpointQueryKey('p1');
    client.setQueryData(key, { data: { id: 'p1', amsUnits: [] } });
    const invalidate = vi.spyOn(client, 'invalidateQueries');
    const { unmount } = renderHook(useFarmHub, {
      wrapper: ({ children }: { children: ReactNode }) => (
        <QueryClientProvider client={client}>{children}</QueryClientProvider>
      ),
    });
    const emit = (percent: number) =>
      act(() =>
        hub.handlers.get('PrinterTelemetryUpdated')?.({
          printerId: 'p1',
          telemetry: {},
          amsUnits: [],
          externalSpoolTray: {
            active: true,
            materialType: 'PLA',
            remainPercent: percent,
          },
        }),
      );
    emit(30);
    invalidate.mockClear();
    emit(0);
    expect(invalidate).not.toHaveBeenCalled();
    expect(client.getQueryData(key)).toMatchObject({
      data: { externalSpoolTray: { active: true, remainPercent: 0 } },
    });
    unmount();
  });
  it('refreshes compatibility only when nozzle diameter or confirmation changes', () => {
    const client = new QueryClient();
    const invalidate = vi.spyOn(client, 'invalidateQueries');
    const { unmount } = renderHook(useFarmHub, {
      wrapper: ({ children }: { children: ReactNode }) => (
        <QueryClientProvider client={client}>{children}</QueryClientProvider>
      ),
    });
    const emit = (diameter: number, confirmed: boolean) =>
      act(() =>
        hub.handlers.get('PrinterTelemetryUpdated')?.({
          printerId: 'p1',
          telemetry: { nozzleDiameterMm: diameter, nozzleConfirmed: confirmed },
          amsUnits: [],
        }),
      );
    emit(0.4, true);
    expect(invalidate).toHaveBeenCalledWith({
      predicate: expect.any(Function),
    });
    invalidate.mockClear();
    emit(0.4, true);
    expect(invalidate).not.toHaveBeenCalled();
    emit(0.6, true);
    expect(invalidate).toHaveBeenCalledTimes(1);
    invalidate.mockClear();
    emit(0.6, false);
    expect(invalidate).toHaveBeenCalledTimes(1);
    unmount();
  });
});
