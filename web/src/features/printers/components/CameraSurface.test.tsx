import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
} from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { cameraFrameQueryKey } from '../hooks/useFarmHub';
import { CameraSurface } from './CameraSurface';

function setup(
  receivedAtUtc?: number,
  props: { isReachable?: boolean; supported?: boolean } = {},
) {
  const client = new QueryClient();
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
    expect(
      screen.getByText('Camera stale · Waiting for frames'),
    ).toBeInTheDocument();
    expect(screen.queryByText('Live')).not.toBeInTheDocument();
  });
  it('expires a frame based on receipt time rather than mount time', () => {
    vi.useFakeTimers();
    setup(Date.now() - 4000);
    expect(screen.getByText('Live')).toBeInTheDocument();
    act(() => vi.advanceTimersByTime(1001));
    expect(screen.queryByText('Live')).not.toBeInTheDocument();
    expect(
      screen.getByText('Camera stale · Waiting for frames'),
    ).toBeInTheDocument();
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
    expect(screen.getByText('Printer offline')).toBeInTheDocument();
    expect(screen.queryByText('Live')).not.toBeInTheDocument();
  });
  it('labels unsupported camera transports', () => {
    setup(undefined, { supported: false });
    expect(screen.getByText('Camera not supported yet')).toBeInTheDocument();
  });
});
