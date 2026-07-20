import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { FilamentProfileResponse } from '@/lib/api/generated/endpoints.schemas';
import { FilamentProfilesTable } from './FilamentProfilesTable';

const profiles: FilamentProfileResponse[] = [
  {
    id: 'f1',
    name: 'Bambu PLA Basic',
    sourceFormat: 'OrcaSlicer',
    baseProfileId: null,
    baseProfileName: null,
    hasUnresolvedBaseInChain: false,
    material: 'PLA',
    brand: 'Bambu',
    colorHex: '#C12E1FFF',
    nozzleSizeMm: null,
    nozzleTempC: 220,
    bedTempC: 55,
    maxVolumetricSpeedMm3s: 21,
    fanPercent: 100,
    compatiblePrinterNames: ['Bambu X1 Carbon 0.4 nozzle'],
  },
  {
    id: 'f2',
    name: 'Bambu PLA Basic - Red',
    sourceFormat: 'OrcaSlicer',
    baseProfileId: 'f1',
    baseProfileName: 'Bambu PLA Basic',
    hasUnresolvedBaseInChain: false,
    material: 'PLA',
    brand: 'Bambu',
    colorHex: '#C12E1FFF',
    nozzleSizeMm: null,
    nozzleTempC: 220,
    bedTempC: 55,
    maxVolumetricSpeedMm3s: 21,
    fanPercent: 100,
    compatiblePrinterNames: [],
  },
];

describe('FilamentProfilesTable', () => {
  it('renders the name and base for each imported filament profile', () => {
    render(<FilamentProfilesTable profiles={profiles} onRowClick={vi.fn()} />);

    const rows = screen.getAllByRole('row');
    // rows[0] is the header row.
    expect(within(rows[1]).getByText('Bambu PLA Basic')).toBeInTheDocument();
    expect(within(rows[1]).getByText('Base')).toBeInTheDocument();
    expect(
      within(rows[2]).getByText('Bambu PLA Basic - Red'),
    ).toBeInTheDocument();
    expect(within(rows[2]).getByText('Bambu PLA Basic')).toBeInTheDocument();
  });

  it('calls onRowClick with the profile id when a row is clicked', async () => {
    const user = userEvent.setup();
    const onRowClick = vi.fn();
    render(
      <FilamentProfilesTable
        profiles={[profiles[0]]}
        onRowClick={onRowClick}
      />,
    );

    await user.click(screen.getByText('Bambu PLA Basic'));

    expect(onRowClick).toHaveBeenCalledWith('f1');
  });
});
