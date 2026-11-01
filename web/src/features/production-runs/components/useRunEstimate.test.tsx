import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { beforeEach, expect, it, vi } from 'vitest';
import { productionPlanFixture } from '@/test-utils/productionPlan';
import { type ProductionPlan, previewPlan } from './productionPlanner';
import { type EstimateLine, useRunEstimate } from './useRunEstimate';

vi.mock('./productionPlanner', async (original) => ({
  ...(await original<typeof import('./productionPlanner')>()),
  previewPlan: vi.fn(),
}));
const preview = vi.mocked(previewPlan);
beforeEach(() => {
  preview.mockReset();
});
const line = (lineId: string, quantity: number): EstimateLine => ({
  lineId,
  targetId: lineId,
  targetKind: 'Part',
  quantity,
  quantityBasis: 'Units',
});
const plan = (quantity: number) =>
  productionPlanFixture(quantity, 'Units') as ProductionPlan;
function mount(lines: EstimateLine[]) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return renderHook(({ lines }) => useRunEstimate(lines), {
    initialProps: { lines },
    wrapper: ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    ),
  });
}
function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => {
    resolve = done;
  });
  return { promise, resolve };
}

it('keeps stable versions through invalid lines, out-of-order responses, and removal', async () => {
  const first = deferred<ProductionPlan>();
  const second = deferred<ProductionPlan>();
  preview.mockImplementation((input) =>
    input.targetId === 'first' ? first.promise : second.promise,
  );
  const { result, rerender } = mount([
    line('invalid', 0),
    line('first', 3),
    line('second', 8),
  ]);
  expect(preview).toHaveBeenCalledTimes(2);
  await act(async () => second.resolve(plan(8)));
  await waitFor(() =>
    expect(result.current.byLine.get('second')?.plan?.version).toBe(
      'plan-Units-8',
    ),
  );
  expect(result.current.byLine.get('first')?.plan).toBeUndefined();
  expect(result.current.isReady).toBe(false);
  rerender({ lines: [line('second', 8)] });
  await waitFor(() => expect(result.current.isReady).toBe(true));
  await act(async () => first.resolve(plan(3)));
  expect([...result.current.byLine.keys()]).toEqual(['second']);
  await waitFor(() =>
    expect(result.current.byLine.get('second')?.plan?.version).toBe(
      'plan-Units-8',
    ),
  );
});

it('does not reuse a previous quantity or basis plan while the current preview is pending', async () => {
  const changed = deferred<ProductionPlan>();
  preview.mockResolvedValueOnce(plan(1)).mockReturnValueOnce(changed.promise);
  const { result, rerender } = mount([line('one', 1)]);
  await waitFor(() => expect(result.current.isReady).toBe(true));
  rerender({ lines: [{ ...line('one', 5), quantityBasis: 'Plates' }] });
  expect(result.current.isPending).toBe(true);
  expect(result.current.byLine.get('one')?.plan).toBeUndefined();
  await act(async () => changed.resolve({ ...plan(20), version: 'plates-5' }));
  await waitFor(() =>
    expect(result.current.byLine.get('one')?.plan?.version).toBe('plates-5'),
  );
});

it('shares identical preview requests while retaining independent line totals', async () => {
  preview.mockResolvedValue(plan(4));
  const first = line('a', 4);
  const { result } = mount([first, { ...first, lineId: 'b' }]);
  await waitFor(() => expect(result.current.isReady).toBe(true));
  expect(preview).toHaveBeenCalledTimes(1);
  expect(result.current.plates).toBe(2);
  expect(result.current.units).toBe(8);
  expect(result.current.byLine.size).toBe(2);
  expect(result.current.partIds).toEqual(['part-1']);
});

it('tracks time and filament completeness independently and preserves known zero estimates', async () => {
  const response = plan(4);
  response.parts[0].minutes = null;
  response.parts[0].grams = 0;
  response.parts.push({
    ...response.parts[0],
    partId: 'base',
    minutes: 30,
    grams: null,
  });
  response.minutes = null;
  response.grams = null;
  response.estimate = {
    time: { value: 30, missing: 1 },
    filament: { value: 0, missing: 1 },
    approximate: true,
    colors: [],
  };
  preview.mockResolvedValue(response);
  const { result } = mount([line('one', 1)]);
  await waitFor(() => expect(result.current.isReady).toBe(true));
  expect(result.current.time).toEqual({ value: 30, missing: 1 });
  expect(result.current.filament).toEqual({ value: 0, missing: 1 });
  expect(result.current.partIds).toEqual(['part-1', 'base']);
});

it('blocks a failed refetch even with cached data, and recovers through Retry', async () => {
  preview
    .mockResolvedValueOnce(plan(4))
    .mockRejectedValueOnce(new Error('Inputs changed'))
    .mockResolvedValueOnce({ ...plan(4), version: 'fresh' });
  const { result } = mount([line('one', 4)]);
  await waitFor(() => expect(result.current.isReady).toBe(true));
  await act(async () => {
    await result.current.refetch();
  });
  await waitFor(() => expect(result.current.isError).toBe(true));
  expect(result.current.isReady).toBe(false);
  expect(result.current.byLine.get('one')?.plan).toBeUndefined();
  await act(async () => {
    await result.current.byLine.get('one')?.refetch();
  });
  await waitFor(() => expect(result.current.isReady).toBe(true));
  expect(result.current.byLine.get('one')?.plan?.version).toBe('fresh');
});
