import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { connectFarmHub } from './farmHubConnection';

const hub = vi.hoisted(() => ({
  start: vi.fn<() => Promise<void>>(),
  stop: vi.fn<() => Promise<void>>(),
  close: (_error?: Error) => {},
  reconnect: () => {},
  policy: {
    nextRetryDelayInMilliseconds: (_context: { previousRetryCount: number }) =>
      0,
  },
}));

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: class {
    withUrl() {
      return this;
    }
    withAutomaticReconnect(policy: typeof hub.policy) {
      hub.policy = policy;
      return this;
    }
    build() {
      return {
        start: hub.start,
        stop: hub.stop,
        onreconnecting: vi.fn(),
        onreconnected: (callback: typeof hub.reconnect) => {
          hub.reconnect = callback;
        },
        onclose: (callback: typeof hub.close) => {
          hub.close = callback;
        },
      };
    }
  },
}));

describe('farm hub recovery', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    hub.start.mockReset().mockResolvedValue();
    hub.stop.mockReset().mockResolvedValue();
  });
  afterEach(() => vi.useRealTimers());

  it('retries initial failures and reports the first successful connection once', async () => {
    hub.start
      .mockRejectedValueOnce(new Error('offline'))
      .mockRejectedValueOnce(new Error('offline'));
    const onConnected = vi.fn();
    const session = connectFarmHub({ onConnected });
    await vi.advanceTimersByTimeAsync(10000);
    expect(hub.start).toHaveBeenCalledTimes(3);
    expect(onConnected).toHaveBeenCalledTimes(1);
    await session.stop();
  });

  it('keeps retrying established connections with a bounded delay', async () => {
    const session = connectFarmHub();
    expect(
      hub.policy.nextRetryDelayInMilliseconds({ previousRetryCount: 100 }),
    ).toBe(30000);
    await session.stop();
  });

  it('restarts after a terminal close and catches up through onReconnected', async () => {
    const onConnected = vi.fn();
    const onReconnected = vi.fn();
    const session = connectFarmHub({ onConnected, onReconnected });
    await vi.advanceTimersByTimeAsync(0);
    hub.close(new Error('closed'));
    await vi.advanceTimersByTimeAsync(5000);
    expect(hub.start).toHaveBeenCalledTimes(2);
    expect(onConnected).toHaveBeenCalledTimes(1);
    expect(onReconnected).toHaveBeenCalledTimes(1);
    await session.stop();
  });

  it('cancels pending retries when unmounted', async () => {
    hub.start.mockRejectedValue(new Error('offline'));
    const session = connectFarmHub();
    await vi.advanceTimersByTimeAsync(0);
    await session.stop();
    await vi.advanceTimersByTimeAsync(60000);
    expect(hub.start).toHaveBeenCalledTimes(1);
    expect(hub.stop).toHaveBeenCalledTimes(1);
  });

  it('waits for an in-flight negotiation during cleanup and suppresses callbacks', async () => {
    let resolveStart!: () => void;
    hub.start.mockImplementation(
      () =>
        new Promise<void>((resolve) => {
          resolveStart = resolve;
        }),
    );
    const onConnected = vi.fn();
    const session = connectFarmHub({ onConnected });
    const stopped = session.stop();
    expect(hub.stop).not.toHaveBeenCalled();
    resolveStart();
    await stopped;
    expect(onConnected).not.toHaveBeenCalled();
    expect(hub.stop).toHaveBeenCalledTimes(1);
    expect(vi.getTimerCount()).toBe(0);
  });
});
