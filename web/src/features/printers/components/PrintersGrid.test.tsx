import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router';
import { describe, expect, it, vi } from 'vitest';
import type { FleetPrinterResponse } from '@/lib/api/generated/endpoints.schemas';
import { PrintersGrid } from './PrintersGrid';

function renderGrid(printers: FleetPrinterResponse[]) {
  const onMarkReady = vi.fn();
  render(
    <MemoryRouter initialEntries={['/printers']}>
      <Routes>
        <Route
          path="/printers"
          element={
            <PrintersGrid
              printers={printers}
              density="small"
              onMarkReady={onMarkReady}
            />
          }
        />
        <Route path="/printers/:id" element={<p>Printer detail</p>} />
      </Routes>
    </MemoryRouter>,
  );
  return onMarkReady;
}

describe('PrintersGrid', () => {
  it('marks an awaiting-clear printer ready from its chip without opening detail', async () => {
    const user = userEvent.setup();
    const onMarkReady = renderGrid([
      {
        id: 'bay-1',
        name: 'Bay 1',
        status: 'AwaitingClear',
        isReachable: true,
      },
    ]);

    const chip = screen.getByRole('button', { name: 'Mark Bay 1 ready' });
    expect(chip).toHaveTextContent('Awaiting Clear');
    expect(chip.closest('a')).toBeNull();
    expect(screen.getAllByRole('button')).toHaveLength(1);
    await user.click(chip);
    expect(onMarkReady).toHaveBeenCalledExactlyOnceWith('bay-1');
    expect(screen.queryByText('Printer detail')).not.toBeInTheDocument();

    await user.click(screen.getByRole('link', { name: 'Open Bay 1' }));
    expect(screen.getByText('Printer detail')).toBeInTheDocument();
  });

  it('keeps idle, printing, and offline status chips non-interactive', () => {
    renderGrid([
      { id: 'idle', name: 'Idle bay', status: 'Idle', isReachable: true },
      {
        id: 'printing',
        name: 'Printing bay',
        status: 'Printing',
        isReachable: true,
      },
      {
        id: 'offline',
        name: 'Offline bay',
        status: 'AwaitingClear',
        isReachable: false,
      },
    ]);

    expect(screen.getByText('Idle')).toBeInTheDocument();
    expect(screen.getByText('Printing')).toBeInTheDocument();
    expect(screen.getByText('Offline')).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });
});
