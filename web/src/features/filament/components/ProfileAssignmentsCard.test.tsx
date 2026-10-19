import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import type { FilamentProfileAssignmentInput } from '@/lib/api/generated/endpoints.schemas';
import { ProfileAssignmentsCard } from './ProfileAssignmentsCard';

vi.mock('@/lib/api/generated/filaments/filaments', () => ({
  useOpenFarmApiEndpointsFilamentsGetFilamentAssignmentFleetEndpoint: () => ({
    data: {
      data: {
        variants: [
          {
            brand: 'Bambu',
            modelCode: 'P1S',
            nozzleDiameterMm: 0.4,
            printerCount: 2,
          },
          {
            brand: 'Bambu',
            modelCode: 'P1S',
            nozzleDiameterMm: 0.6,
            printerCount: 1,
          },
          {
            brand: 'Bambu',
            modelCode: 'X1C',
            nozzleDiameterMm: 0.6,
            printerCount: 1,
          },
        ],
        printersAwaitingNozzle: ['Printer awaiting telemetry'],
      },
    },
  }),
  useOpenFarmApiEndpointsFilamentsGetFilamentProfileAssignmentsEndpoint:
    vi.fn(),
  useOpenFarmApiEndpointsFilamentsUpdateFilamentProfileAssignmentsEndpoint:
    vi.fn(),
}));
vi.mock('@/features/profiles/components/ProfilePickerCombobox', () => ({
  ProfilePickerCombobox: ({
    ariaLabel,
    value,
    onChange,
  }: {
    ariaLabel: string;
    value?: string;
    onChange: (id: string | null) => void;
  }) => (
    <select
      aria-label={ariaLabel}
      value={value ?? ''}
      onChange={(e) => onChange(e.target.value || null)}
    >
      <option value="">None</option>
      <option value="default">PLA default</option>
      <option value="black">Black PLA</option>
    </select>
  ),
}));

const parent: FilamentProfileAssignmentInput = {
  brand: 'Bambu',
  modelCode: 'P1S',
  nozzleDiameterMm: 0.4,
  filamentProfileId: 'default',
};

describe('Profile assignments card', () => {
  it('shows hardware groups and sets one default without touching other nozzles or color overrides', async () => {
    const user = userEvent.setup();
    const changed = vi.fn();
    const blackOverride = {
      ...parent,
      filamentProfileId: 'black',
      colorHex: '#000000',
    };
    function Editor() {
      const [assignments, setAssignments] = useState<
        FilamentProfileAssignmentInput[]
      >([parent, { ...parent, nozzleDiameterMm: 0.6 }, blackOverride]);
      return (
        <ProfileAssignmentsCard
          materialType="PLA"
          variants={[{ colorName: 'Black', colorHex: '#000000' }]}
          assignments={assignments}
          onChange={(value) => {
            setAssignments(value);
            changed(value);
          }}
        />
      );
    }
    render(<Editor />);
    expect(screen.getByText('P1S · 0.4 mm')).toBeInTheDocument();
    expect(screen.getByText('P1S · 0.6 mm')).toBeInTheDocument();
    expect(screen.getByText('X1C · 0.6 mm')).toBeInTheDocument();
    expect(screen.getByText(/2 printers/)).toBeInTheDocument();
    expect(screen.getByText(/Printer awaiting telemetry/)).toBeInTheDocument();
    // Color overrides are edited per variant, not here.
    expect(
      screen.queryByLabelText('Black profile for P1S · 0.4 mm'),
    ).not.toBeInTheDocument();

    await user.selectOptions(
      screen.getByLabelText('Default profile for X1C · 0.6 mm'),
      'default',
    );
    expect(changed).toHaveBeenLastCalledWith([
      parent,
      { ...parent, nozzleDiameterMm: 0.6 },
      blackOverride,
      {
        brand: 'Bambu',
        modelCode: 'X1C',
        nozzleDiameterMm: 0.6,
        filamentProfileId: 'default',
        colorHex: null,
      },
    ]);

    await user.selectOptions(
      screen.getByLabelText('Default profile for P1S · 0.4 mm'),
      '',
    );
    expect(changed).toHaveBeenLastCalledWith(
      expect.not.arrayContaining([parent]),
    );
    expect(changed.mock.lastCall?.[0]).toContainEqual(blackOverride);
  });

  it('shows an invalid saved assignment with the backend reason', () => {
    render(
      <ProfileAssignmentsCard
        materialType="PLA"
        variants={[]}
        assignments={[parent]}
        issues={[
          {
            assignment: parent,
            message: 'The selected profile is no longer selectable.',
          },
        ]}
        onChange={vi.fn()}
      />,
    );
    expect(screen.getByText(/Invalid assignment/)).toBeInTheDocument();
    expect(
      screen.getByText(/The selected profile is no longer selectable/),
    ).toBeInTheDocument();
  });
});
