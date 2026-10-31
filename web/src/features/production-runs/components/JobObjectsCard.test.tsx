import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { JobObjectsCard } from './JobObjectsCard';

const api = vi.hoisted(() => ({
  read: vi.fn(),
  skip: vi.fn(),
  output: vi.fn(),
  requeue: vi.fn(),
  writeOff: vi.fn(),
  notStarted: vi.fn(),
}));
vi.mock('@/lib/api/generated/print-jobs/print-jobs', () => ({
  openFarmApiEndpointsPrintJobsGetJobObjectsEndpoint: api.read,
  openFarmApiEndpointsPrintJobsConfirmPrintNotStartedEndpoint: api.notStarted,
  openFarmApiEndpointsPrintJobsSkipJobObjectsEndpoint: api.skip,
  openFarmApiEndpointsPrintJobsConfirmObjectOutputEndpoint: api.output,
  openFarmApiEndpointsPrintJobsRequeuePrintJobEndpoint: api.requeue,
  openFarmApiEndpointsPrintJobsWriteOffPrintJobEndpoint: api.writeOff,
}));
const execution = {
  artifactHash: 'artifact',
  observed: true,
  selectedUnitIds: ['1', '2'],
  requestedObjectIds: [],
  confirmedObjectIds: [],
  plannedExclusions: [],
  manifest: {
    objects: [
      { id: 'A', name: 'Bracket A' },
      { id: 'B', name: 'Bracket B' },
    ],
    units: [
      { id: '1', objectIds: ['A'] },
      { id: '2', objectIds: ['B'] },
    ],
  },
};
beforeEach(() => {
  vi.resetAllMocks();
  api.read.mockResolvedValue({ data: execution });
  api.skip.mockResolvedValue({ data: execution });
  api.output.mockResolvedValue({ data: execution });
});
function setup(status = 'Printing') {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  });
  render(
    <QueryClientProvider client={client}>
      <JobObjectsCard jobId="job" status={status} produced={0} target={2} />
    </QueryClientProvider>,
  );
  return userEvent.setup();
}

it('supports keyboard unit selection, confirmation cancellation, and focus return', async () => {
  const user = setup();
  const box = await screen.findByRole('checkbox', { name: /Unit 1:/ });
  box.focus();
  await user.keyboard('[Space]');
  const trigger = screen.getByRole('button', { name: 'Skip selected units' });
  expect(trigger).toBeEnabled();
  trigger.focus();
  await user.keyboard('{Enter}');
  expect(await screen.findByRole('alertdialog')).toBeVisible();
  await user.keyboard('{Escape}');
  await waitFor(() => expect(trigger).toHaveFocus());
  expect(api.skip).not.toHaveBeenCalled();
  await user.keyboard('{Enter}');
  await user.click(await screen.findByRole('button', { name: 'Skip units' }));
  await waitFor(() =>
    expect(api.skip).toHaveBeenCalledWith('job', {
      artifactHash: 'artifact',
      unitIds: ['1'],
    }),
  );
});

it('disables conflicting controls while awaiting printer confirmation', async () => {
  api.read.mockResolvedValue({
    data: { ...execution, observed: false, requestedObjectIds: ['A'] },
  });
  setup();
  expect(
    await screen.findByRole('checkbox', { name: /Unit 2:/ }),
  ).toBeDisabled();
  expect(
    screen.getByRole('button', { name: 'Skip selected units' }),
  ).toBeDisabled();
  expect(
    screen.getByRole('button', { name: 'Clear selection' }),
  ).toBeDisabled();
});

it('shows unavailable hardware support before allowing a skip selection', async () => {
  api.read.mockResolvedValue({
    data: {
      ...execution,
      canSkipObjects: false,
      skipUnavailableReason: 'This firmware needs validation.',
    },
  });
  setup();
  expect(
    await screen.findByText('This firmware needs validation.'),
  ).toBeVisible();
  expect(screen.getByRole('checkbox', { name: /Unit 1:/ })).toBeDisabled();
  expect(
    screen.getByRole('button', { name: 'Skip selected units' }),
  ).toBeDisabled();
});

it('requires explicit physical verification before resolving an unconfirmed start', async () => {
  api.read.mockResolvedValue({ data: { ...execution, startRequested: true } });
  api.notStarted.mockResolvedValue({});
  const user = setup('Queued');
  const trigger = await screen.findByRole('button', {
    name: 'Confirm print did not start',
  });
  expect(api.notStarted).not.toHaveBeenCalled();
  await user.click(trigger);
  expect(
    await screen.findByText(/Confirm that the printer is idle/),
  ).toBeVisible();
  await user.click(screen.getByRole('button', { name: 'Confirm not started' }));
  await waitFor(() =>
    expect(api.notStarted).toHaveBeenCalledWith('job', {
      artifactHash: 'artifact',
    }),
  );
});

it('confirms only the physically finished units after an unverified completion', async () => {
  api.read.mockResolvedValue({
    data: { ...execution, observed: false, requestedObjectIds: ['A'] },
  });
  const user = setup('Completed');
  const box = await screen.findByRole('checkbox', { name: /Unit 2:/ });
  box.focus();
  await user.keyboard('[Space]');
  await user.click(
    screen.getByRole('button', { name: 'Confirm produced units' }),
  );
  await user.click(
    await screen.findByRole('button', { name: 'Confirm output' }),
  );
  await waitFor(() =>
    expect(api.output).toHaveBeenCalledWith('job', {
      artifactHash: 'artifact',
      producedUnitIds: ['2'],
    }),
  );
  expect(api.requeue).not.toHaveBeenCalled();
});
