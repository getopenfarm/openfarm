import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { ProcessProfileResponse } from '@/lib/api/generated/endpoints.schemas';
import { ProcessProfilesTable } from './ProcessProfilesTable';

const profiles: ProcessProfileResponse[] = [
  {
    id: 'p1',
    name: '0.20mm Standard @BIQU B1',
    sourceFormat: 'OrcaSlicer',
    baseProfileId: null,
    baseProfileName: null,
    hasUnresolvedBaseInChain: false,
    layerHeightMm: 0.2,
    wallLoops: 3,
    compatiblePrinterNames: [],
  },
  {
    id: 'p2',
    name: '0.16mm Optimal @BIQU B1',
    sourceFormat: 'OrcaSlicer',
    baseProfileId: 'p1',
    baseProfileName: '0.20mm Standard @BIQU B1',
    hasUnresolvedBaseInChain: false,
    layerHeightMm: 0.16,
    wallLoops: 3,
    compatiblePrinterNames: [],
  },
];

describe('ProcessProfilesTable', () => {
  it('renders the name, layer height, and base for each imported process profile', () => {
    render(<ProcessProfilesTable profiles={profiles} onRowClick={vi.fn()} />);

    const rows = screen.getAllByRole('row');
    // rows[0] is the header row.
    expect(
      within(rows[1]).getByText('0.20mm Standard @BIQU B1'),
    ).toBeInTheDocument();
    expect(within(rows[1]).getByText('0.2mm')).toBeInTheDocument();
    expect(within(rows[1]).getByText('Base')).toBeInTheDocument();
    expect(
      within(rows[2]).getByText('0.16mm Optimal @BIQU B1'),
    ).toBeInTheDocument();
    expect(
      within(rows[2]).getByText('0.20mm Standard @BIQU B1'),
    ).toBeInTheDocument();
  });

  it('calls onRowClick with the profile id when a row is clicked', async () => {
    const user = userEvent.setup();
    const onRowClick = vi.fn();
    render(
      <ProcessProfilesTable profiles={[profiles[0]]} onRowClick={onRowClick} />,
    );

    await user.click(screen.getByText('0.20mm Standard @BIQU B1'));

    expect(onRowClick).toHaveBeenCalledWith('p1');
  });
});
