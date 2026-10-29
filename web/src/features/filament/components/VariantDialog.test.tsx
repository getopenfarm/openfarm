import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { FilamentProfileAssignmentInput } from '@/lib/api/generated/endpoints.schemas';
import { VariantDialog } from './VariantDialog';

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
            modelCode: 'X1C',
            nozzleDiameterMm: 0.6,
            printerCount: 1,
          },
        ],
        printersAwaitingNozzle: [],
      },
    },
  }),
}));
vi.mock('@/features/profiles/components/ProfilePickerCombobox', () => ({
  ProfilePickerCombobox: ({
    id,
    value,
    placeholder,
    onChange,
  }: {
    id?: string;
    value?: string;
    placeholder?: string;
    onChange: (id: string | null) => void;
  }) => (
    <select
      id={id}
      value={value ?? ''}
      onChange={(e) => onChange(e.target.value || null)}
    >
      <option value="">{placeholder}</option>
      <option value="silk">PLA Silk</option>
    </select>
  ),
}));

const P1S_DEFAULT: FilamentProfileAssignmentInput = {
  brand: 'Bambu',
  modelCode: 'P1S',
  nozzleDiameterMm: 0.4,
  filamentProfileId: 'basic',
  colorHex: null,
};

const SAVED_BLACK = {
  id: 'var-1',
  colorName: 'Galaxy Black',
  colorHex: '#18181b',
  pricePerKg: null,
  lowStockGrams: null,
  lowStockDays: null,
};

function renderDialog(
  props: Partial<React.ComponentProps<typeof VariantDialog>> = {},
) {
  const onSave = vi.fn();
  const onOpenChange = vi.fn();
  render(
    <VariantDialog
      open
      onOpenChange={onOpenChange}
      inheritedPrice={25.5}
      inheritedLowStock={null}
      inheritedLowStockDays={null}
      materialType="PLA"
      assignments={[P1S_DEFAULT]}
      onSave={onSave}
      {...props}
    />,
  );
  return { onSave, onOpenChange };
}

describe('VariantDialog', () => {
  it('saves a price and a profile override keyed to the color', async () => {
    const user = userEvent.setup();
    const { onSave } = renderDialog({ variant: SAVED_BLACK });

    const price = screen.getByLabelText('Price per kg');
    expect(price).toHaveAttribute('placeholder', 'Default: USD 25.50');
    // Saved variants keep their name and color.
    expect(screen.queryByLabelText('Name')).not.toBeInTheDocument();
    expect(
      screen.queryByText('Name and color are fixed once saved.'),
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole('option', { name: 'Use default profile' }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('option', { name: 'No default assigned' }),
    ).toBeInTheDocument();

    await user.type(price, '30');
    await user.selectOptions(
      screen.getByLabelText('X1C · 0.6 mm profile'),
      'silk',
    );
    await user.click(screen.getByRole('button', { name: 'Update variant' }));

    expect(onSave).toHaveBeenCalledWith({
      variant: { ...SAVED_BLACK, pricePerKg: 30 },
      overrides: [
        {
          brand: 'Bambu',
          modelCode: 'X1C',
          nozzleDiameterMm: 0.6,
          filamentProfileId: 'silk',
          colorHex: '#18181b',
        },
      ],
    });
  });

  it('clears an existing override back to the default', async () => {
    const user = userEvent.setup();
    const { onSave } = renderDialog({
      variant: { ...SAVED_BLACK, pricePerKg: 30 },
      assignments: [
        P1S_DEFAULT,
        { ...P1S_DEFAULT, filamentProfileId: 'silk', colorHex: '#18181b' },
      ],
    });

    expect(screen.getByLabelText('P1S · 0.4 mm profile')).toHaveValue('silk');
    await user.click(
      screen.getByRole('button', { name: 'Use default for P1S · 0.4 mm' }),
    );
    await user.clear(screen.getByLabelText('Price per kg'));
    await user.click(screen.getByRole('button', { name: 'Update variant' }));

    expect(onSave).toHaveBeenCalledWith({
      variant: { ...SAVED_BLACK, pricePerKg: null },
      overrides: [],
    });
  });

  it('adds a new variant with a name and color', async () => {
    const user = userEvent.setup();
    const { onSave } = renderDialog();

    await user.click(screen.getByRole('button', { name: 'Red' }));
    expect(screen.getByLabelText('Name')).toHaveValue('Red');
    await user.click(screen.getByRole('button', { name: 'Add variant' }));

    expect(onSave).toHaveBeenCalledWith({
      variant: {
        colorName: 'Red',
        colorHex: '#ef4444',
        pricePerKg: null,
        lowStockGrams: null,
        lowStockDays: null,
      },
      overrides: [],
    });
  });

  it('shows overrides immediately when adding', async () => {
    const user = userEvent.setup();
    const { onSave } = renderDialog();

    const price = screen.getByLabelText('Price per kg');
    expect(price).toBeVisible();
    expect(screen.getByLabelText('P1S · 0.4 mm profile')).toBeVisible();
    expect(
      screen.queryByRole('button', { name: /Overrides/ }),
    ).not.toBeInTheDocument();

    await user.click(price);
    await user.keyboard('30');
    await user.tab();
    expect(screen.getByLabelText('Low stock below')).toHaveFocus();
    await user.tab();
    expect(screen.getByLabelText('Days left below')).toHaveFocus();
    await user.tab();
    expect(screen.getByLabelText('P1S · 0.4 mm profile')).toHaveFocus();
    await user.click(screen.getByRole('button', { name: 'Add variant' }));

    expect(onSave).toHaveBeenCalledWith(
      expect.objectContaining({
        variant: expect.objectContaining({ pricePerKg: 30 }),
      }),
    );
  });

  it('shows existing overrides for a staged variant', () => {
    renderDialog({
      variant: {
        colorName: 'Red',
        colorHex: '#ef4444',
        pricePerKg: 28,
        lowStockGrams: null,
        lowStockDays: null,
      },
    });

    expect(screen.getByLabelText('Price per kg')).toHaveValue(28);
  });

  it('blocks a negative price', async () => {
    const user = userEvent.setup();
    const { onSave } = renderDialog({ variant: SAVED_BLACK });

    await user.type(screen.getByLabelText('Price per kg'), '-1');

    expect(screen.getByText('Price must be non-negative')).toBeVisible();
    expect(
      screen.getByRole('button', { name: 'Update variant' }),
    ).toBeDisabled();
    expect(onSave).not.toHaveBeenCalled();
  });

  it('discards edits on cancel', async () => {
    const user = userEvent.setup();
    const { onSave, onOpenChange } = renderDialog({ variant: SAVED_BLACK });

    await user.type(screen.getByLabelText('Price per kg'), '30');
    await user.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(onSave).not.toHaveBeenCalled();
  });
  it('saves its own low-stock threshold, showing the filament default as the hint', async () => {
    const user = userEvent.setup();
    const { onSave } = renderDialog({
      variant: SAVED_BLACK,
      inheritedLowStock: 1500,
    });

    const lowStock = screen.getByLabelText('Low stock below');
    expect(lowStock).toHaveAttribute('placeholder', 'Default: 1.5 kg');
    await user.type(lowStock, '250');
    await user.click(screen.getByRole('button', { name: 'Update variant' }));

    expect(onSave).toHaveBeenCalledWith(
      expect.objectContaining({
        variant: expect.objectContaining({ lowStockGrams: 250 }),
      }),
    );
  });

  it('blocks a negative low-stock threshold', async () => {
    const user = userEvent.setup();
    const { onSave } = renderDialog({ variant: SAVED_BLACK });

    await user.type(screen.getByLabelText('Low stock below'), '-1');

    expect(screen.getByText('Must be 0 or more')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Update variant' }),
    ).toBeDisabled();
    expect(onSave).not.toHaveBeenCalled();
  });

  it('saves its own days threshold, showing the filament default as the hint', async () => {
    const user = userEvent.setup();
    const { onSave } = renderDialog({
      variant: SAVED_BLACK,
      inheritedLowStockDays: 14,
    });

    const days = screen.getByLabelText('Days left below');
    expect(days).toHaveAttribute('placeholder', 'Default: 14 d');
    await user.type(days, '7');
    await user.click(screen.getByRole('button', { name: 'Update variant' }));

    expect(onSave).toHaveBeenCalledWith(
      expect.objectContaining({
        variant: expect.objectContaining({ lowStockDays: 7 }),
      }),
    );
  });

  it('blocks a days threshold over a year', async () => {
    const user = userEvent.setup();
    const { onSave } = renderDialog({ variant: SAVED_BLACK });

    await user.type(screen.getByLabelText('Days left below'), '366');

    expect(screen.getByText('Whole days, 0 to 365')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Update variant' }),
    ).toBeDisabled();
    expect(onSave).not.toHaveBeenCalled();
  });
});
