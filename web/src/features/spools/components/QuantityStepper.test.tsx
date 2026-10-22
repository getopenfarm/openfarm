import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { QuantityStepper } from './QuantityStepper';

function setup(value: string) {
  const onValueChange = vi.fn();
  render(
    <QuantityStepper
      aria-label="Quantity"
      value={value}
      onValueChange={onValueChange}
      min={1}
      max={50}
    />,
  );
  return onValueChange;
}

describe('QuantityStepper', () => {
  it.each([
    ['3', 'More', '4'],
    ['3', 'Fewer', '2'],
    // A blank field counts as the minimum.
    ['', 'More', '2'],
    // A typed fraction steps to the whole number beyond it.
    ['2.5', 'Fewer', '2'],
    ['2.5', 'More', '3'],
    // At a limit, a press keeps the value there.
    ['1', 'Fewer', '1'],
    ['50', 'More', '50'],
    // Outside the limits, a press brings it back in.
    ['99', 'Fewer', '50'],
    ['0', 'More', '1'],
  ])('from "%s", %s gives %s', async (value, button, expected) => {
    const user = userEvent.setup();
    const onValueChange = setup(value);

    await user.click(screen.getByRole('button', { name: button }));

    expect(onValueChange).toHaveBeenCalledWith(expected);
  });

  it('reports what is typed as it is, so the form can explain a bad value', async () => {
    const user = userEvent.setup();
    const onValueChange = setup('');

    await user.type(screen.getByLabelText('Quantity'), '7');

    expect(onValueChange).toHaveBeenCalledWith('7');
  });

  it('marks a button inactive at its limit, but leaves it focusable', () => {
    setup('1');

    const fewer = screen.getByRole('button', { name: 'Fewer' });
    expect(fewer).toHaveAttribute('aria-disabled', 'true');
    expect(fewer).toBeEnabled();
    expect(screen.getByRole('button', { name: 'More' })).toHaveAttribute(
      'aria-disabled',
      'false',
    );
  });
});
