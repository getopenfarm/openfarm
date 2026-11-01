import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AddTasksDialog } from './AddTasksDialog';

const TASKS = [
  { id: 't1', name: 'Carbon Rod Cleaning', everyDays: 30, printerIds: [] },
  {
    id: 't2',
    name: 'Full Calibration Run',
    everyPrintHours: 100,
    printerIds: ['p1'],
  },
  { id: 't3', name: 'Camera Lens Cleaning', everyDays: 30, printerIds: ['p2'] },
];

function stubFetch(tasks = TASKS) {
  const posts: unknown[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === 'POST') posts.push(JSON.parse(init.body as string));
      const body = input.toString().includes('/maintenance/tasks')
        ? { items: tasks }
        : { entityIds: ['p1'], count: 1 };
      return Promise.resolve(
        new Response(JSON.stringify(body), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      );
    }),
  );
  return posts;
}

describe('AddTasksDialog', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('starts unchecked, hides enrolled tasks and enrolls the checked', async () => {
    const posts = stubFetch();
    render(
      <QueryClientProvider client={new QueryClient()}>
        <MemoryRouter>
          <AddTasksDialog printerId="p1" open onOpenChange={() => {}} />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    const rods = await screen.findByRole('checkbox', {
      name: /Carbon Rod Cleaning/,
    });
    expect(rods).not.toBeChecked();
    expect(
      screen.getByRole('checkbox', { name: /Camera Lens Cleaning/ }),
    ).not.toBeChecked();
    expect(screen.queryByText('Full Calibration Run')).not.toBeInTheDocument();
    expect(screen.getAllByText('30 d')).toHaveLength(2);

    await userEvent.click(rods);
    await userEvent.click(screen.getByRole('button', { name: 'Add 1' }));
    await waitFor(() => expect(posts).toEqual([{ taskIds: ['t1'] }]));
  });

  it('selects all available tasks', async () => {
    const posts = stubFetch();
    render(
      <QueryClientProvider client={new QueryClient()}>
        <MemoryRouter>
          <AddTasksDialog printerId="p1" open onOpenChange={() => {}} />
        </MemoryRouter>
      </QueryClientProvider>,
    );
    await userEvent.click(
      await screen.findByRole('button', { name: 'Select all' }),
    );
    await userEvent.click(screen.getByRole('button', { name: 'Add 2' }));
    await waitFor(() => expect(posts).toEqual([{ taskIds: ['t1', 't3'] }]));
  });

  it('says no tasks exist when the farm has none', async () => {
    stubFetch([]);
    render(
      <QueryClientProvider client={new QueryClient()}>
        <MemoryRouter>
          <AddTasksDialog printerId="p1" open onOpenChange={() => {}} />
        </MemoryRouter>
      </QueryClientProvider>,
    );
    expect(await screen.findByText('No tasks yet.')).toBeInTheDocument();
  });
});
