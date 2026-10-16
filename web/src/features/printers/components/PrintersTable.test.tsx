import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { describe, expect, it, vi } from 'vitest';
import { PrintersTable } from './PrintersTable';

describe('PrintersTable', () => {
  it('shows awaiting-clear status without readiness controls and supports detail navigation', async () => {
    const onOpen = vi.fn();
    render(
      <MemoryRouter>
        <PrintersTable
          printers={[
            {
              id: 'ready',
              name: 'P01',
              status: 'AwaitingClear',
              isReachable: true,
            },
            { id: 'printing', name: 'P02', status: 'Printing' },
          ]}
          maintenanceByPrinterId={new Map()}
          onOpen={onOpen}
        />
      </MemoryRouter>,
    );
    expect(
      screen.queryByRole('button', { name: 'Actions for P02' }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Clear' }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Actions for P01' }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('columnheader', { name: 'Actions' }),
    ).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'P01' })).toHaveAttribute(
      'href',
      '/printers/ready',
    );
    await userEvent.click(screen.getByText('Awaiting clear'));
    expect(onOpen).toHaveBeenCalledWith('ready');
  });
});
