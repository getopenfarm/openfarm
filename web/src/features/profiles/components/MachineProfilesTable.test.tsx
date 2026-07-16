import { render, screen } from '@testing-library/react';
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
];

describe('MachineProfilesTable', () => {
  it('renders key fields for each imported machine profile', () => {
    render(<MachineProfilesTable profiles={profiles} onRowClick={vi.fn()} />);

    expect(screen.getByText('Bambu X1 Carbon 0.4 nozzle')).toBeInTheDocument();
    expect(screen.getByText('OrcaSlicer')).toBeInTheDocument();
    expect(screen.getByText('0.4mm')).toBeInTheDocument();
    expect(screen.getByText('256×256mm')).toBeInTheDocument();
    expect(screen.getByText('Yes')).toBeInTheDocument();
  });

  it('shows an empty state when no profiles have been imported', () => {
    render(<MachineProfilesTable profiles={[]} onRowClick={vi.fn()} />);

    expect(
      screen.getByText(
        'No machine profiles yet. Import a slicer bundle to populate this catalog.',
      ),
    ).toBeInTheDocument();
  });

  it('calls onRowClick with the profile id when a row is clicked', async () => {
    const user = userEvent.setup();
    const onRowClick = vi.fn();
    render(
      <MachineProfilesTable profiles={profiles} onRowClick={onRowClick} />,
    );

    await user.click(screen.getByText('Bambu X1 Carbon 0.4 nozzle'));

    expect(onRowClick).toHaveBeenCalledWith('m1');
  });
});
