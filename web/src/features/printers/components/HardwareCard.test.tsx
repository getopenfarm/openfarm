import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { HardwareCard } from './HardwareCard';

const p1s = {
  name: 'Bambu Lab P1S',
  driver: 'Bambu LAN',
  extruders: ['Nozzle'],
  cameraSupported: true,
};
const u1 = {
  name: 'Snapmaker U1',
  driver: 'Moonraker',
  extruders: ['T1', 'T2', 'T3', 'T4'],
  cameraSupported: true,
};

describe('HardwareCard', () => {
  it('shows a single nozzle without its temperature', () => {
    render(
      <HardwareCard
        model={p1s}
        extruders={[
          {
            index: 0,
            nozzleDiameterMm: 0.4,
            nozzleConfirmed: true,
            tempC: 220,
          },
        ]}
      />,
    );

    expect(screen.getByText('Bambu Lab P1S')).toBeInTheDocument();
    expect(screen.getByText('Bambu LAN')).toBeInTheDocument();
    expect(screen.getByText('0.4 mm (reported)')).toBeInTheDocument();
    expect(screen.queryByText(/°C/)).not.toBeInTheDocument();
  });

  it('shows each head of a toolchanger, mixed sizes and one awaiting', () => {
    render(
      <HardwareCard
        model={u1}
        extruders={[
          {
            index: 0,
            nozzleDiameterMm: 0.4,
            nozzleConfirmed: true,
            tempC: 215,
          },
          { index: 1, nozzleDiameterMm: 0.6, nozzleConfirmed: true, tempC: 30 },
          { index: 2, nozzleDiameterMm: 0.4, nozzleConfirmed: false },
        ]}
      />,
    );

    expect(screen.getByText('0.4 mm (reported) · 215 °C')).toBeInTheDocument();
    expect(screen.getByText('0.6 mm (reported) · 30 °C')).toBeInTheDocument();
    expect(
      screen.getByText('0.4 mm (awaiting confirmation)'),
    ).toBeInTheDocument();
    expect(screen.getByText('Awaiting telemetry')).toBeInTheDocument();
    expect(screen.getByText(/^T4:/)).toBeInTheDocument();
  });

  it('renders nothing for a printer the catalog no longer knows', () => {
    const { container } = render(<HardwareCard model={null} />);

    expect(container).toBeEmptyDOMElement();
  });
});
