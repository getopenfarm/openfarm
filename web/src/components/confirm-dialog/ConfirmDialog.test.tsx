import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ConfirmDialog } from './ConfirmDialog';

function renderDialog(overrides?: {
  onOpenChange?: (open: boolean) => void;
  onConfirm?: () => void;
}) {
  const onOpenChange = overrides?.onOpenChange ?? vi.fn();
  const onConfirm = overrides?.onConfirm ?? vi.fn();
  render(
    <ConfirmDialog
      open
      onOpenChange={onOpenChange}
      title="Delete part?"
      description="This can't be undone."
      confirmLabel="Delete"
      onConfirm={onConfirm}
    />,
  );
  return { onOpenChange, onConfirm };
}

describe('ConfirmDialog', () => {
  it('renders the title and description', () => {
    renderDialog();
    expect(screen.getByText('Delete part?')).toBeInTheDocument();
    expect(screen.getByText("This can't be undone.")).toBeInTheDocument();
  });

  it('calls onConfirm once when the confirm action is clicked', async () => {
    const user = userEvent.setup();
    const { onConfirm } = renderDialog();

    await user.click(screen.getByRole('button', { name: 'Delete' }));

    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  it('calls onOpenChange(false) without calling onConfirm when cancelled', async () => {
    const user = userEvent.setup();
    const { onOpenChange, onConfirm } = renderDialog();

    await user.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(onConfirm).not.toHaveBeenCalled();
  });
});
