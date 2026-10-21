import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { SlicingReadinessResponse } from '@/lib/api/generated/endpoints.schemas';
import { SlicerReadinessPanel } from './SlicerReadinessPanel';

const readiness = vi.fn();
vi.mock('@/lib/api/generated/slicing/slicing', () => ({
  useOpenFarmApiEndpointsSlicingGetSlicingReadinessEndpoint: (
    params: unknown,
    options: unknown,
  ) => readiness(params, options),
}));

const blocked: SlicingReadinessResponse = {
  engine: 'BambuStudio',
  version: '2.7.1',
  isReady: false,
  printers: [
    {
      printerId: 'p1',
      printerName: 'P1S Lab',
      issues: [
        {
          kind: 'Process',
          code: 'Missing',
          message:
            "Process profile '0.20mm Standard @BBL X1C' is not in Bambu Studio 2.7.1. Select another profile, or pin the part to another slicer.",
          link: '/printers/p1/edit',
        },
      ],
    },
    { printerId: 'p2', printerName: 'P1S Garage', issues: [] },
  ],
  filaments: [
    {
      filamentId: 'f1',
      filamentName: 'PLA Basic',
      brand: 'Bambu',
      modelCode: 'P1S',
      nozzleDiameterMm: 0.4,
      profileName: 'Bambu PLA Basic @BBL X1C',
      printerIds: ['p2'],
      issues: [
        {
          kind: 'Filament',
          code: 'Missing',
          message:
            "Filament profile 'Bambu PLA Basic @BBL X1C' is not in Bambu Studio 2.7.1. Select another profile, or pin the part to another slicer.",
          link: '/filament/f1/edit',
        },
      ],
    },
  ],
};

function renderPanel(props: { printerId?: string } = {}) {
  render(
    <MemoryRouter>
      <SlicerReadinessPanel engine="BambuStudio" version="2.7.1" {...props} />
    </MemoryRouter>,
  );
}

describe('Slicer readiness panel', () => {
  beforeEach(() => readiness.mockReset());

  it('asks for the exact slicer and shows nothing while loading', () => {
    readiness.mockReturnValue({ isLoading: true });

    renderPanel();

    expect(readiness).toHaveBeenCalledWith(
      { engine: 'BambuStudio', version: '2.7.1' },
      { query: { enabled: true } },
    );
    expect(screen.queryByTestId('slicer-readiness-warning')).toBeNull();
  });

  it('says when every printer and filament resolves', () => {
    readiness.mockReturnValue({
      isLoading: false,
      data: {
        data: { ...blocked, isReady: true, printers: [], filaments: [] },
      },
    });

    renderPanel();

    expect(
      screen.getByText(
        'Every printer and filament resolves in Bambu Studio 2.7.1.',
      ),
    ).toBeTruthy();
  });

  it('lists each blocked printer and filament with the message and a link to fix it', () => {
    readiness.mockReturnValue({ isLoading: false, data: { data: blocked } });

    renderPanel();

    expect(
      screen.getByText('Production is blocked with Bambu Studio 2.7.1'),
    ).toBeTruthy();
    expect(
      screen.getByText(
        /Process profile '0.20mm Standard @BBL X1C' is not in Bambu Studio 2.7.1/,
      ),
    ).toBeTruthy();
    expect(screen.getByText('P1S Lab:')).toBeTruthy();
    expect(screen.getByText('PLA Basic · P1S 0.4 mm:')).toBeTruthy();
    const links = screen.getAllByRole('link', { name: 'Fix' });
    expect(links.map((link) => link.getAttribute('href'))).toEqual([
      '/printers/p1/edit',
      '/filament/f1/edit',
    ]);
  });

  it("shows only one printer's problems and the filament assignments that apply to it", () => {
    readiness.mockReturnValue({ isLoading: false, data: { data: blocked } });

    renderPanel({ printerId: 'p2' });

    expect(screen.queryByText('P1S Lab:')).toBeNull();
    expect(screen.getByText('PLA Basic · P1S 0.4 mm:')).toBeTruthy();
  });

  it('a printer with no problems of its own reads as resolved', () => {
    readiness.mockReturnValue({
      isLoading: false,
      data: { data: { ...blocked, filaments: [] } },
    });

    renderPanel({ printerId: 'p2' });

    expect(
      screen.getByText('This printer resolves in Bambu Studio 2.7.1.'),
    ).toBeTruthy();
  });

  it('does not claim the slicer is fine when the check itself failed', () => {
    readiness.mockReturnValue({ isLoading: false, isError: true });

    renderPanel();

    expect(
      screen.getByText('Could not check Bambu Studio 2.7.1.'),
    ).toBeTruthy();
  });
});
