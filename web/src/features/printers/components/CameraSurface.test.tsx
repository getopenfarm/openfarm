import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
} from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { authenticatedFetch } from '@/lib/auth/session';
import {
  CameraSurface,
  cameraFrameQueryKey,
  formatCameraAge,
} from './CameraSurface';

vi.mock('@/lib/auth/session', () => ({ authenticatedFetch: vi.fn() }));
const fetchFrame = vi.mocked(authenticatedFetch);
const notModified = () => new Response(null, { status: 304 });
function jpeg(etag: string, receivedAtUtc: number) {
  return new Response(new Blob([etag], { type: 'image/jpeg' }), {
    status: 200,
    headers: {
      'Content-Type': 'image/jpeg',
      ETag: etag,
      'X-Frame-Received-At': new Date(receivedAtUtc).toISOString(),
    },
  });
}
const src = (name: string) => `data:image/jpeg;base64,${name}`;
/** Lets the mount poll settle, so a frame written to the cache afterwards isn't overwritten by its 304. */
async function settleFirstPoll() {
  await waitFor(() => expect(fetchFrame).toHaveBeenCalled());
  await act(async () => {});
}
beforeEach(() => {
  fetchFrame.mockReset();
  fetchFrame.mockImplementation(async () => notModified());
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
      src: src('frame'),
      etag: '"frame"',
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
    await settleFirstPoll();
    act(() =>
      client.setQueryData(cameraFrameQueryKey('p1'), {
        receivedAtUtc: Date.now(),
        src: src('new'),
        etag: '"new"',
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

describe('Camera polling', () => {
  it('loads the frame as a JPEG and revalidates it with its ETag, keeping it on 304', async () => {
    const receivedAtUtc = Date.now();
    fetchFrame.mockImplementationOnce(async () => jpeg('"a"', receivedAtUtc));
    setup();
    await waitFor(() =>
      expect(screen.getByRole('img').getAttribute('src')).toMatch(
        /^data:image\/jpeg;base64,/,
      ),
    );
    const first = screen.getByRole('img').getAttribute('src');
    expect(screen.getByText('Live')).toBeInTheDocument();
    await waitFor(() => expect(fetchFrame).toHaveBeenCalledTimes(2), {
      timeout: 3000,
    });
    expect(fetchFrame).toHaveBeenLastCalledWith(
      '/api/v1/printers/p1/camera.jpg',
      expect.objectContaining({ headers: { 'If-None-Match': '"a"' } }),
    );
    expect(screen.getByRole('img').getAttribute('src')).toBe(first);
  });
  it("loads an offline printer's last frame once without calling it live", async () => {
    fetchFrame.mockImplementationOnce(async () => jpeg('"saved"', Date.now()));
    setup(undefined, { isReachable: false });
    await waitFor(() =>
      expect(screen.getByRole('img')).toHaveAttribute(
        'alt',
        'P1S-01 camera last received frame',
      ),
    );
    expect(screen.queryByText('Live')).not.toBeInTheDocument();
    expect(screen.getByText('0s ago')).toHaveAttribute(
      'title',
      expect.stringContaining('Printer offline'),
    );
    await new Promise((resolve) => setTimeout(resolve, 1200));
    expect(fetchFrame).toHaveBeenCalledTimes(1);
  });
  it('does not poll an unsupported camera', async () => {
    setup(undefined, { supported: false });
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(fetchFrame).not.toHaveBeenCalled();
  });
  it('keeps the last decoded frame when a corrupt frame arrives and recovers', async () => {
    const client = setup(Date.now() - 120000, { isReachable: false });
    await settleFirstPoll();
    fireEvent.load(screen.getByRole('img'));
    act(() =>
      client.setQueryData(cameraFrameQueryKey('p1'), {
        src: src('broken'),
        receivedAtUtc: Date.now(),
        etag: '"broken"',
      }),
    );
    await waitFor(() =>
      expect(screen.getByRole('img')).toHaveAttribute('src', src('broken')),
    );
    fireEvent.error(screen.getByRole('img'));
    expect(screen.getByRole('img')).toHaveAttribute('src', src('frame'));
    expect(screen.getByText('2m ago')).toBeInTheDocument();
    act(() =>
      client.setQueryData(cameraFrameQueryKey('p1'), {
        src: src('recovered'),
        receivedAtUtc: Date.now(),
        etag: '"recovered"',
      }),
    );
    await waitFor(() =>
      expect(screen.getByRole('img')).toHaveAttribute('src', src('recovered')),
    );
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
