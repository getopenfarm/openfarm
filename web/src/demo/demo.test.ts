import { afterAll, describe, expect, it, vi } from 'vitest';
import { handle } from './api';
import { assemblies, jobs, printers } from './farm';
import { startSimulator } from './simulator';

const call = async (method: string, path: string, body?: unknown) => {
  const pending = handle(
    new Request(`http://demo.test/api/v1${path}`, {
      method,
      body: body === undefined ? undefined : JSON.stringify(body),
    }),
  );
  await vi.advanceTimersByTimeAsync(400);
  return pending;
};

describe('marketing-site demo API', () => {
  vi.useFakeTimers();
  afterAll(() => vi.useRealTimers());

  it('boots a session with every permission', async () => {
    expect((await call('GET', '/auth/csrf')).status).toBe(200);
    const { token } = await (await call('POST', '/auth/refresh')).json();
    const exp = JSON.parse(atob(token.split('.')[1])).exp * 1000;
    expect(exp).toBeGreaterThan(Date.now() + 86_400_000);
    const whoami = await (await call('GET', '/auth/whoami')).json();
    expect(whoami.permissions).toContain('printers:mark-ready');
  });

  it('refuses what the demo cannot do, with a toast-ready detail', async () => {
    const response = await call('POST', '/printers', {});
    expect(response.status).toBe(403);
    expect((await response.json()).detail).toBe('Not available in the demo.');
    expect((await call('GET', '/nothing-here')).status).toBe(404);
  });

  it('quotes an assembly for the Build panel and Pricing', async () => {
    const quote = await (
      await call('POST', '/assemblies/quote', {
        assemblyId: assemblies[0].id,
        pricing: { mode: 'Markup', value: 100 },
      })
    ).json();
    expect(quote.total.available).toBe(true);
    expect(quote.total.amount).toBeGreaterThan(0);
    expect(quote.colors[0].name).toContain(' · ');
    expect(quote.profiles[0].pricing.price).toBeCloseTo(
      quote.total.amount * 2,
      1,
    );
  });

  it('answers the connected apps tab and the admin apps section', async () => {
    const own = await (await call('GET', '/auth/apps')).json();
    expect(own.apps).toHaveLength(1);
    expect(own.apps[0].clientName).toBe('Claude');
    const admin = await call('GET', `/admin/users/${own.apps[0].id}/apps`);
    expect(await admin.json()).toEqual([]);
  });

  it('sends the next matching queued plate to a cleared printer', async () => {
    startSimulator();
    const printer = printers.find((p) => p.name === 'P1S-03');
    expect(printer?.status).toBe('AwaitingClear');
    const response = await call('POST', `/printers/${printer?.id}/mark-ready`);
    expect(response.status).toBe(200);
    expect(printer?.status).toBe('Idle');
    await vi.advanceTimersByTimeAsync(7000);
    const job = jobs.find((j) => j.id === printer?.jobId);
    // The lid needs PLA Matte, which P1S-03 does not hold; the knob's PLA Basic Black is loaded.
    expect(printer?.status).toBe('Printing');
    expect(job?.name).toBe('Control knob');
    expect(job?.status).toBe('Printing');
  });
});
