import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { expect, it } from 'vitest';
import { DatePicker } from './date-picker';

function Example({ disabled = false }: { disabled?: boolean }) {
  const [value, setValue] = useState('2027-05-03');
  return (
    <>
      <label htmlFor="expiry">Expires</label>
      <DatePicker
        id="expiry"
        value={value}
        onChange={setValue}
        min="2027-05-03"
        disabled={disabled}
        placeholder="Never expires"
        clearLabel="Remove the expiry date"
      />
      <output aria-label="Selected expiry">{value}</output>
    </>
  );
}
it('uses a calendar popover, limits earlier dates, and closes after selecting a date', async () => {
  const user = userEvent.setup();
  const { container } = render(<Example />);
  expect(container.querySelector('input[type="date"]')).toBeNull();
  await user.click(screen.getByLabelText('Expires'));
  const calendar = screen.getByRole('dialog', { name: 'Choose expiry date' });
  expect(
    within(calendar).getByRole('button', { name: /Saturday, May 1st, 2027/ }),
  ).toBeDisabled();
  await user.click(
    within(calendar).getByRole('button', { name: /Tuesday, May 4th, 2027/ }),
  );
  expect(screen.getByLabelText('Selected expiry')).toHaveTextContent(
    '2027-05-04',
  );
  await waitFor(() =>
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument(),
  );
  expect(screen.getByLabelText('Expires')).toHaveFocus();
});
it('clears with the separate X button without opening the calendar', async () => {
  const user = userEvent.setup();
  render(<Example />);
  await user.click(
    screen.getByRole('button', { name: 'Remove the expiry date' }),
  );
  expect(screen.getByLabelText('Selected expiry')).toBeEmptyDOMElement();
  expect(screen.getByLabelText('Expires')).toHaveTextContent('Never expires');
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  expect(screen.getByLabelText('Expires')).toHaveFocus();
});
it('supports keyboard day selection and Escape returns focus to the trigger', async () => {
  const user = userEvent.setup();
  render(<Example />);
  screen.getByLabelText('Expires').focus();
  await user.keyboard('{Enter}');
  await waitFor(() =>
    expect(
      screen.getByRole('button', { name: /Monday, May 3rd, 2027/ }),
    ).toHaveFocus(),
  );
  await user.keyboard('{ArrowRight}{Enter}');
  expect(screen.getByLabelText('Selected expiry')).toHaveTextContent(
    '2027-05-04',
  );
  await user.keyboard('{Enter}{Escape}');
  await waitFor(() =>
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument(),
  );
  expect(screen.getByLabelText('Expires')).toHaveFocus();
});
it('disables calendar entry and removes the clear action while saving', () => {
  render(<Example disabled />);
  expect(screen.getByLabelText('Expires')).toBeDisabled();
  expect(
    screen.queryByRole('button', { name: 'Remove the expiry date' }),
  ).not.toBeInTheDocument();
});
