import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { PrinterTile, type PrinterTileProps } from './PrinterTile';

const baseProps: PrinterTileProps = {
  name: 'Bay 1',
  status: 'Idle',
  isReachable: true,
  filamentColorHex: null,
  progressPercent: 0,
  nozzleTempC: null,
  bedTempC: null,
  etaMinutes: null,
  jobName: null,
  stageLabel: null,
};

describe('PrinterTile', () => {
  it('shows the printer-reported job, progress, temps and ETA while printing', () => {
    render(
      <PrinterTile
        {...baseProps}
        status="Printing"
        progressPercent={62}
        nozzleTempC={220}
        bedTempC={60}
        etaMinutes={72}
        jobName="benchy.3mf"
        stageLabel="Heating bed"
      />,
    );

    expect(screen.getByText('benchy.3mf')).toBeInTheDocument();
    expect(screen.getByText(/Heating bed/)).toBeInTheDocument();
    expect(screen.getByText('220°C / 60°C')).toBeInTheDocument();
    expect(screen.getByText('ETA 1h 12m')).toBeInTheDocument();
    // Progress % appears both on the thumbnail and next to the bar.
    expect(screen.getAllByText('62%').length).toBeGreaterThan(0);
  });

  it('shows "No active print" and hides progress/ETA when idle', () => {
    render(<PrinterTile {...baseProps} status="Idle" />);

    expect(screen.getByText('No active print')).toBeInTheDocument();
    expect(screen.queryByText(/ETA/)).not.toBeInTheDocument();
  });

  it('keeps the job name and hides the thumbnail in the small size', () => {
    render(
      <PrinterTile
        {...baseProps}
        size="small"
        status="Printing"
        progressPercent={62}
        jobName="benchy.3mf"
      />,
    );

    // Job name still shown so operators can see what's printing even in the dense layout.
    expect(screen.getByText('benchy.3mf')).toBeInTheDocument();
    // No thumbnail in small size.
    expect(
      document.querySelector('[data-slot="checkerboard-thumbnail"]'),
    ).not.toBeInTheDocument();
  });

  it('surfaces the offline state distinctly from status', () => {
    render(
      <PrinterTile
        {...baseProps}
        status="Printing"
        isReachable={false}
        jobName="benchy.3mf"
      />,
    );

    expect(screen.getByText('Offline')).toBeInTheDocument();
    // An unreachable printer is not treated as actively printing.
    expect(screen.queryByText(/ETA/)).not.toBeInTheDocument();
  });
});
