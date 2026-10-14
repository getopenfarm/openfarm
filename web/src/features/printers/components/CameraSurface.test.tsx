import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
} from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { openFarmApiEndpointsPrintersGetPrinterCameraSnapshotEndpoint } from '@/lib/api/generated/printers/printers';
import { cameraFrameQueryKey } from '../hooks/useFarmHub';
import { CameraSurface, formatCameraAge } from './CameraSurface';

vi.mock('@/lib/api/generated/printers/printers', () => ({
  openFarmApiEndpointsPrintersGetPrinterCameraSnapshotEndpoint: vi.fn(),
}));
beforeEach(() => {
  vi.mocked(
    openFarmApiEndpointsPrintersGetPrinterCameraSnapshotEndpoint,
  ).mockReset();
  vi.mocked(
    openFarmApiEndpointsPrintersGetPrinterCameraSnapshotEndpoint,
  ).mockResolvedValue({
    data: undefined,
    status: 204,
    headers: new Headers(),
  });
});

function setup(
  receivedAtUtc?: number,
  props: { isReachable?: boolean; supported?: boolean } = {},
) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  if (receivedAtUtc != null)
    client.setQueryData(cameraFrameQueryKey('p1'), {
      receivedAtUtc,
      frameJpegBase64: 'frame',
    });
  render(
    <QueryClientProvider client={client}>
      <CameraSurface printerId="p1" name="P1S-01" {...props} />
    </QueryClientProvider>,
  );
  return client;
}
afterEach(() => vi.useRealTimers());
describe('Camera frame freshness', () => {
  it('does not revive an old cached frame as live when mounting', () => {
    setup(Date.now() - 6000);
    expect(screen.getByText('6s ago')).toBeInTheDocument();
    expect(screen.queryByText('Live')).not.toBeInTheDocument();
  });
  it('expires a frame based on receipt time rather than mount time', () => {
    vi.useFakeTimers();
    setup(Date.now() - 4000);
    expect(screen.getByText('Live')).toBeInTheDocument();
    act(() => vi.advanceTimersByTime(1001));
    expect(screen.queryByText('Live')).not.toBeInTheDocument();
    expect(screen.getByText('5s ago')).toBeInTheDocument();
  });
  it('recovers from unavailable when a new frame arrives', async () => {
    const client = setup();
    expect(screen.getByText('Camera unavailable')).toBeInTheDocument();
    act(() =>
      client.setQueryData(cameraFrameQueryKey('p1'), {
        receivedAtUtc: Date.now(),
        frameJpegBase64: 'new',
      }),
    );
    await waitFor(() => expect(screen.getByText('Live')).toBeInTheDocument());
  });
  it('matches the container to the decoded frame aspect ratio', () => {
    setup(Date.now());
    const image = screen.getByRole('img');
    Object.defineProperties(image, {
      naturalWidth: { value: 640, configurable: true },
      naturalHeight: { value: 480, configurable: true },
    });
    fireEvent.load(image);
    expect(
      Number.parseFloat(image.parentElement?.style.aspectRatio ?? ''),
    ).toBeCloseTo(4 / 3);
    Object.defineProperties(image, {
      naturalWidth: { value: 1280, configurable: true },
      naturalHeight: { value: 720, configurable: true },
    });
    fireEvent.load(image);
    expect(
      Number.parseFloat(image.parentElement?.style.aspectRatio ?? ''),
    ).toBeCloseTo(16 / 9);
  });
  it('hides invalid images and does not label them live', () => {
    setup(Date.now());
    fireEvent.error(screen.getByRole('img'));
    expect(
      screen.getByText('Camera unavailable · Invalid frame'),
    ).toBeInTheDocument();
    expect(screen.queryByText('Live')).not.toBeInTheDocument();
  });
  it('does not show fresh frames as live when the printer is offline', () => {
    setup(Date.now(), { isReachable: false });
    expect(screen.getByText('0s ago')).toBeInTheDocument();
    expect(screen.getByRole('img')).toHaveAttribute(
      'alt',
      'P1S-01 camera last received frame',
    );
    expect(screen.queryByText('Live')).not.toBeInTheDocument();
  });
  it('labels unsupported camera transports', () => {
    setup(undefined, { supported: false });
    expect(screen.getByText('Camera not supported yet')).toBeInTheDocument();
  });
});

describe('Persistent camera snapshot', () => {
  it('loads a shared snapshot without calling it live, even when recent', async () => {
    vi.mocked(
      openFarmApiEndpointsPrintersGetPrinterCameraSnapshotEndpoint,
    ).mockResolvedValue({
      data: {
        frameJpegBase64: 'saved',
        receivedAtUtc: new Date().toISOString(),
      },
      status: 200,
      headers: new Headers(),
    });
    setup(undefined, { isReachable: false });
    await waitFor(() =>
      expect(screen.getByRole('img')).toHaveAttribute(
        'src',
        'data:image/jpeg;base64,saved',
      ),
    );
    expect(screen.queryByText('Live')).not.toBeInTheDocument();
    expect(screen.queryByText('Printer offline')).not.toBeInTheDocument();
    expect(screen.getByRole('img')).not.toHaveClass('grayscale', 'opacity-30');
    expect(screen.getByText('0s ago')).toHaveAttribute(
      'title',
      expect.stringContaining('Printer offline'),
    );
  });
  it('does not replace a newer live frame with a delayed snapshot', async () => {
    let finish!: (
      value: Awaited<
        ReturnType<
          typeof openFarmApiEndpointsPrintersGetPrinterCameraSnapshotEndpoint
        >
      >,
    ) => void;
    vi.mocked(
      openFarmApiEndpointsPrintersGetPrinterCameraSnapshotEndpoint,
    ).mockReturnValue(
      new Promise((resolve) => {
        finish = resolve;
      }),
    );
    const client = setup();
    act(() =>
      client.setQueryData(cameraFrameQueryKey('p1'), {
        frameJpegBase64: 'new',
        receivedAtUtc: Date.now(),
      }),
    );
    await waitFor(() =>
      expect(screen.getByRole('img')).toHaveAttribute(
        'src',
        'data:image/jpeg;base64,new',
      ),
    );
    await act(async () =>
      finish({
        data: {
          frameJpegBase64: 'old',
          receivedAtUtc: new Date(Date.now() - 60000).toISOString(),
        },
        status: 200,
        headers: new Headers(),
      }),
    );
    expect(screen.getByRole('img')).toHaveAttribute(
      'src',
      'data:image/jpeg;base64,new',
    );
    expect(screen.getByText('Live')).toBeInTheDocument();
  });
  it('keeps the last decoded frame when a corrupt frame arrives and recovers', async () => {
    const client = setup(Date.now() - 120000);
    fireEvent.load(screen.getByRole('img'));
    act(() =>
      client.setQueryData(cameraFrameQueryKey('p1'), {
        frameJpegBase64: 'broken',
        receivedAtUtc: Date.now(),
      }),
    );
    await waitFor(() =>
      expect(screen.getByRole('img')).toHaveAttribute(
        'src',
        'data:image/jpeg;base64,broken',
      ),
    );
    fireEvent.error(screen.getByRole('img'));
    expect(screen.getByRole('img')).toHaveAttribute(
      'src',
      'data:image/jpeg;base64,frame',
    );
    expect(screen.getByText('2m ago')).toBeInTheDocument();
    expect(screen.queryByText('Live')).not.toBeInTheDocument();
    act(() =>
      client.setQueryData(cameraFrameQueryKey('p1'), {
        frameJpegBase64: 'recovered',
        receivedAtUtc: Date.now(),
      }),
    );
    await waitFor(() =>
      expect(screen.getByRole('img')).toHaveAttribute(
        'src',
        'data:image/jpeg;base64,recovered',
      ),
    );
    expect(screen.getByText('Live')).toBeInTheDocument();
  });
  it('updates the age without receiving another frame', () => {
    vi.useFakeTimers();
    setup(Date.now() - 59000, { isReachable: false });
    expect(screen.getByText('59s ago')).toBeInTheDocument();
    act(() => vi.advanceTimersByTime(1000));
    expect(screen.getByText('1m ago')).toBeInTheDocument();
    act(() => vi.advanceTimersByTime(3540000));
    expect(screen.getByText('1h ago')).toBeInTheDocument();
  });
  it.each([
    [0, '0s ago'],
    [-1000, '0s ago'],
    [59000, '59s ago'],
    [60000, '1m ago'],
    [3600000, '1h ago'],
    [86400000, '1d ago'],
  ])('formats %s milliseconds as %s', (ms, expected) => {
    expect(formatCameraAge(ms)).toBe(expected);
  });
});
