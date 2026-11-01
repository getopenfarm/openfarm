import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { expect, it, vi } from 'vitest';
import {
  LibraryItemPicker,
  type LibraryPickerGroup,
} from './LibraryItemPicker';

const groups: LibraryPickerGroup[] = [
  {
    kind: 'Part',
    heading: 'Parts',
    items: [{ id: 'part-1', name: 'Bracket' }],
  },
  {
    kind: 'Assembly',
    heading: 'Assemblies',
    items: [{ id: 'assembly-1', name: 'Desk kit' }],
  },
];
const defaults = {
  groups,
  onPick: vi.fn(),
  isPending: false,
  isError: false,
  onRetry: vi.fn(),
  shortcuts: true,
};

it('opens with slash, searches and picks with the keyboard, and returns focus to the trigger', async () => {
  const onPick = vi.fn();
  render(<LibraryItemPicker {...defaults} onPick={onPick} />);
  const user = userEvent.setup();
  await user.keyboard('/');
  const search = screen.getByRole('combobox', {
    name: 'Search parts and assemblies',
  });
  expect(search).toHaveFocus();
  await user.type(search, 'Desk');
  expect(
    screen.queryByRole('option', { name: 'Bracket' }),
  ).not.toBeInTheDocument();
  await user.keyboard('{ArrowDown}{Enter}');
  expect(onPick).toHaveBeenCalledWith({
    kind: 'Assembly',
    id: 'assembly-1',
    name: 'Desk kit',
  });
  expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
  expect(
    screen.getByRole('button', { name: 'Add part or assembly' }),
  ).toHaveFocus();
});

it('preserves typing and supports Ctrl+K and Escape consistently', async () => {
  render(
    <>
      <input aria-label="Run or assembly name" />
      <LibraryItemPicker {...defaults} />
    </>,
  );
  const user = userEvent.setup();
  const name = screen.getByRole('textbox', { name: 'Run or assembly name' });
  await user.type(name, 'Order/42');
  expect(name).toHaveValue('Order/42');
  expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
  await user.keyboard('{Control>}k{/Control}');
  expect(screen.getByRole('combobox')).toHaveFocus();
  await user.keyboard('{Escape}');
  expect(
    screen.getByRole('button', { name: 'Add part or assembly' }),
  ).toHaveFocus();
});

it('does not intercept shortcuts from another dialog or while disabled', async () => {
  const { rerender } = render(
    <>
      <div role="dialog" aria-label="Other action">
        <button type="button">Dialog action</button>
      </div>
      <LibraryItemPicker {...defaults} />
    </>,
  );
  const user = userEvent.setup();
  screen.getByRole('button', { name: 'Dialog action' }).focus();
  await user.keyboard('/');
  expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
  rerender(<LibraryItemPicker {...defaults} disabled />);
  await user.keyboard('/');
  expect(
    screen.getByRole('button', { name: 'Add part or assembly' }),
  ).toBeDisabled();
  expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
});

it('keeps ineligible targets visible and skips them during keyboard selection', async () => {
  const onPick = vi.fn();
  render(
    <LibraryItemPicker
      {...defaults}
      onPick={onPick}
      groups={[
        {
          ...groups[0],
          items: [
            {
              ...groups[0].items[0],
              disabled: true,
              description: 'Print requirements incomplete',
            },
          ],
        },
        groups[1],
      ]}
    />,
  );
  const user = userEvent.setup();
  await user.click(
    screen.getByRole('button', { name: 'Add part or assembly' }),
  );
  expect(screen.getByRole('option', { name: /Bracket/ })).toHaveAttribute(
    'aria-disabled',
    'true',
  );
  await user.keyboard('{ArrowDown}{Enter}');
  expect(onPick).toHaveBeenCalledWith({
    kind: 'Assembly',
    id: 'assembly-1',
    name: 'Desk kit',
  });
});

it('shows loading, error/retry, and empty states and focuses search after retry', async () => {
  const onRetry = vi.fn();
  const { rerender } = render(
    <LibraryItemPicker {...defaults} isPending onRetry={onRetry} />,
  );
  const user = userEvent.setup();
  await user.click(
    screen.getByRole('button', { name: 'Add part or assembly' }),
  );
  expect(
    screen.getByRole('status', { name: 'Loading parts and assemblies' }),
  ).toBeInTheDocument();
  rerender(<LibraryItemPicker {...defaults} isError onRetry={onRetry} />);
  const retry = screen.getByRole('button', { name: 'Retry' });
  retry.focus();
  await user.keyboard('{Enter}');
  expect(onRetry).toHaveBeenCalledTimes(1);
  rerender(<LibraryItemPicker {...defaults} />);
  expect(screen.getByRole('combobox')).toHaveFocus();
  rerender(<LibraryItemPicker {...defaults} groups={[]} />);
  expect(screen.getByText('No parts or assemblies')).toBeInTheDocument();
});
