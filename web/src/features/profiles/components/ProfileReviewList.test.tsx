import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import type {
  FilamentPresetPreviewItem,
  MachinePresetPreviewItem,
} from '@/lib/api/generated/endpoints.schemas';
import {
  ProfileReviewList,
  type ProfileSelectionMap,
  profileSelectionKey,
} from './ProfileReviewList';

const machinePresets: MachinePresetPreviewItem[] = [
  {
    name: 'Base Printer',
    sourceFormat: 'OrcaSlicer',
    baseProfileName: null,
    hasUnresolvedBaseInChain: false,
    isDuplicate: false,
    nozzleSizeMm: 0.4,
    bedSizeXMm: null,
    bedSizeYMm: null,
    maxHeightMm: null,
    hasEnclosure: null,
    supportedMaterials: [],
  },
];

const filamentPresets: FilamentPresetPreviewItem[] = [
  {
    name: 'Existing PLA',
    sourceFormat: 'OrcaSlicer',
    baseProfileName: null,
    hasUnresolvedBaseInChain: false,
    isDuplicate: true,
    material: 'PLA',
    brand: null,
    colorHex: null,
    nozzleSizeMm: null,
    nozzleTempC: null,
    bedTempC: null,
    maxVolumetricSpeedMm3s: null,
    fanPercent: null,
    compatiblePrinterNames: [],
  },
  {
    name: 'Existing PLA - Red',
    sourceFormat: 'OrcaSlicer',
    baseProfileName: 'Existing PLA',
    hasUnresolvedBaseInChain: false,
    isDuplicate: false,
    material: 'PLA',
    brand: null,
    colorHex: '#C12E1FFF',
    nozzleSizeMm: null,
    nozzleTempC: null,
    bedTempC: null,
    maxVolumetricSpeedMm3s: null,
    fanPercent: null,
    compatiblePrinterNames: [],
  },
];

function StatefulReviewList() {
  const [selections, setSelections] = useState<ProfileSelectionMap>({
    [profileSelectionKey('Machine', 'Base Printer')]: { selected: true },
    [profileSelectionKey('Filament', 'Existing PLA')]: { selected: true },
  });

  return (
    <ProfileReviewList
      machinePresets={machinePresets}
      filamentPresets={filamentPresets}
      selections={selections}
      onChange={(key, next) =>
        setSelections((prev) => ({ ...prev, [key]: next }))
      }
    />
  );
}

describe('ProfileReviewList', () => {
  it('renders name, base/override indicator, and duplicate flag', () => {
    render(<StatefulReviewList />);

    expect(screen.getByText('Base Printer')).toBeInTheDocument();
    expect(screen.getByText('Existing PLA')).toBeInTheDocument();
    expect(screen.getAllByText('Base')).toHaveLength(2);
    expect(
      screen.getByRole('combobox', {
        name: 'Duplicate action for Existing PLA',
      }),
    ).toBeInTheDocument();
  });

  it('does not render a process presets section', () => {
    render(<StatefulReviewList />);

    expect(screen.queryByText('Process profiles')).not.toBeInTheDocument();
  });

  it('unchecking a preset toggles its selection state', async () => {
    const user = userEvent.setup();
    render(<StatefulReviewList />);

    const checkbox = screen.getByRole('checkbox', {
      name: 'Select Base Printer',
    });
    expect(checkbox).toBeChecked();

    await user.click(checkbox);
    expect(checkbox).not.toBeChecked();
  });

  it('choosing "Import as new" for a duplicate updates its duplicate action', async () => {
    const user = userEvent.setup();
    render(<StatefulReviewList />);

    const select = screen.getByRole('combobox', {
      name: 'Duplicate action for Existing PLA',
    });
    await user.click(select);
    await user.click(
      await screen.findByRole('option', { name: 'Import as new' }),
    );

    expect(select).toHaveTextContent('Import as new');
  });

  it('FR-006: warns when a selected override is checked but its bundle-present base is unchecked', async () => {
    const user = userEvent.setup();
    render(<StatefulReviewList />);

    const warningText =
      'Base "Existing PLA" is in this bundle but not selected.';
    expect(screen.queryByText(warningText)).not.toBeInTheDocument();

    await user.click(
      screen.getByRole('checkbox', { name: 'Select Existing PLA' }),
    );

    expect(screen.getByText(warningText)).toBeInTheDocument();

    await user.click(
      screen.getByRole('checkbox', { name: 'Select Existing PLA' }),
    );

    expect(screen.queryByText(warningText)).not.toBeInTheDocument();
  });
});
