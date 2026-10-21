import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { describe, expect, it, vi } from 'vitest';
import { PrinterSlicerReadinessCard } from './PrinterSlicerReadinessCard';

vi.mock('@/lib/api/generated/settings/settings', () => ({
  useOpenFarmApiEndpointsSettingsGetSlicingDefaultsEndpoint: () => ({
    data: {
      data: {
        available: [
          { engine: 'OrcaSlicer', version: '2.4.2', platform: 'Linux' },
          { engine: 'BambuStudio', version: '2.7.1', platform: 'Linux' },
        ],
      },
    },
  }),
}));
vi.mock('@/lib/api/generated/slicing/slicing', () => ({
  useOpenFarmApiEndpointsSlicingGetSlicingReadinessEndpoint: (params: {
    engine: string;
  }) => ({
    isLoading: false,
    data: {
      data: {
        printers: [
          {
            printerId: 'p1',
            printerName: 'P1S Lab',
            issues:
              params.engine === 'BambuStudio'
                ? [
                    {
                      kind: 'Process',
                      code: 'Missing',
                      message: 'Process profile is not in Bambu Studio 2.7.1.',
                      link: '/printers/p1/edit',
                    },
                  ]
                : [],
          },
        ],
        filaments: [],
      },
    },
  }),
}));

describe('Printer slicer readiness card', () => {
  it('shows, per bundled slicer, whether this printer can print with it', () => {
    render(
      <MemoryRouter>
        <PrinterSlicerReadinessCard printerId="p1" />
      </MemoryRouter>,
    );

    expect(screen.getByText('Slicer readiness')).toBeInTheDocument();
    expect(screen.getByText('OrcaSlicer 2.4.2')).toBeInTheDocument();
    expect(screen.getByText('Bambu Studio 2.7.1')).toBeInTheDocument();
    expect(
      screen.getByText('This printer resolves in OrcaSlicer 2.4.2.'),
    ).toBeInTheDocument();
    expect(
      screen.getByText('Production is blocked with Bambu Studio 2.7.1'),
    ).toBeInTheDocument();
  });
});
