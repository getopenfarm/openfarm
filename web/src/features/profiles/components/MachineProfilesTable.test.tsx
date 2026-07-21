import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { MachineProfileResponse } from '@/lib/api/generated/endpoints.schemas';
import { MachineProfilesTable } from './MachineProfilesTable';

const profiles: MachineProfileResponse[] = [
  {
    id: 'm1',
    name: 'Bambu X1 Carbon 0.4 nozzle',
    sourceFormat: 'OrcaSlicer',
    baseProfileId: null,
    baseProfileName: null,
    hasUnresolvedBaseInChain: false,
    nozzleSizeMm: 0.4,
    bedSizeXMm: 256,
    bedSizeYMm: 256,
    maxHeightMm: 256,
    hasEnclosure: true,
    supportedMaterials: [],
  },
  {
    id: 'm2',
    name: 'Bambu X1 Carbon 0.4 nozzle - Direct Drive',
    sourceFormat: 'OrcaSlicer',
    baseProfileId: 'm1',
    baseProfileName: 'Bambu X1 Carbon 0.4 nozzle',
    hasUnresolvedBaseInChain: false,
    nozzleSizeMm: 0.4,
    bedSizeXMm: 256,
    bedSizeYMm: 256,
    maxHeightMm: 256,
    hasEnclosure: true,
    supportedMaterials: [],
  },
];

describe('MachineProfilesTable', () => {
  it('renders the name and base for each imported machine profile', () => {
    render(<MachineProfilesTable profiles={profiles} onRowClick={vi.fn()} />);

    const rows = screen.getAllByRole('row');
    // rows[0] is the header row.
    expect(
      within(rows[1]).getByText('Bambu X1 Carbon 0.4 nozzle'),
    ).toBeInTheDocument();
    expect(within(rows[1]).getByText('Base')).toBeInTheDocument();
    expect(
      within(rows[2]).getByText('Bambu X1 Carbon 0.4 nozzle - Direct Drive'),
    ).toBeInTheDocument();
    expect(
      within(rows[2]).getByText('Bambu X1 Carbon 0.4 nozzle'),
    ).toBeInTheDocument();
  });

  it('calls onRowClick with the profile id when a row is clicked', async () => {
    const user = userEvent.setup();
    const onRowClick = vi.fn();
    render(
      <MachineProfilesTable profiles={[profiles[0]]} onRowClick={onRowClick} />,
    );

    await user.click(screen.getByText('Bambu X1 Carbon 0.4 nozzle'));

    expect(onRowClick).toHaveBeenCalledWith('m1');
  });
});
