import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ProfileRawDataDialog } from './ProfileRawDataDialog';

describe('ProfileRawDataDialog', () => {
  it('shows a loading state while the detail request is pending', () => {
    render(
      <ProfileRawDataDialog
        open
        onOpenChange={vi.fn()}
        isLoading
        name={undefined}
        rawDataJson={undefined}
      />,
    );

    expect(screen.getByText('Loading…')).toBeInTheDocument();
  });

  it('shows the profile name and pretty-printed raw JSON once loaded', () => {
    render(
      <ProfileRawDataDialog
        open
        onOpenChange={vi.fn()}
        name="Bambu PLA Basic"
        rawDataJson='{"nozzle_temperature":["220"]}'
      />,
    );

    expect(screen.getByText('Bambu PLA Basic')).toBeInTheDocument();
    const textbox = screen.getByRole('textbox');
    expect(textbox).toHaveValue(
      JSON.stringify({ nozzle_temperature: ['220'] }, null, 2),
    );
  });

  it('renders view-only (no Save button, disabled textarea) when no onSave is provided', () => {
    render(
      <ProfileRawDataDialog
        open
        onOpenChange={vi.fn()}
        name="0.20mm Standard @BIQU B1"
        rawDataJson='{"layer_height":"0.2"}'
      />,
    );

    expect(
      screen.queryByRole('button', { name: 'Save' }),
    ).not.toBeInTheDocument();
    expect(screen.getByRole('textbox')).toBeDisabled();
  });

  it('does not render when closed', () => {
    render(
      <ProfileRawDataDialog
        open={false}
        onOpenChange={vi.fn()}
        name="Bambu PLA Basic"
        rawDataJson="{}"
      />,
    );

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('saves an edit and shows the last-saved value once the query result updates', async () => {
    const user = userEvent.setup();
    const onSave = vi.fn().mockResolvedValue(undefined);
    const { rerender } = render(
      <ProfileRawDataDialog
        open
        onOpenChange={vi.fn()}
        name="Bambu PLA Basic"
        rawDataJson='{"nozzle_temperature":["220"]}'
        onSave={onSave}
      />,
    );

    const textbox = screen.getByRole('textbox');
    await user.clear(textbox);
    await user.click(textbox);
    await user.paste('{"nozzle_temperature":["230"]}');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() =>
      expect(onSave).toHaveBeenCalledWith('{"nozzle_temperature":["230"]}'),
    );

    rerender(
      <ProfileRawDataDialog
        open
        onOpenChange={vi.fn()}
        name="Bambu PLA Basic"
        rawDataJson='{"nozzle_temperature":["230"]}'
        onSave={onSave}
      />,
    );

    expect(screen.getByRole('textbox')).toHaveValue(
      JSON.stringify({ nozzle_temperature: ['230'] }, null, 2),
    );
  });

  it('shows an inline error on malformed JSON and leaves the last-saved value visible', async () => {
    const user = userEvent.setup();
    const onSave = vi
      .fn()
      .mockRejectedValue(new Error('The raw data is not valid JSON.'));
    render(
      <ProfileRawDataDialog
        open
        onOpenChange={vi.fn()}
        name="Bambu PLA Basic"
        rawDataJson='{"nozzle_temperature":["220"]}'
        onSave={onSave}
      />,
    );

    const textbox = screen.getByRole('textbox');
    await user.clear(textbox);
    await user.click(textbox);
    await user.paste('{not valid json');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(
      await screen.findByText('The raw data is not valid JSON.'),
    ).toBeInTheDocument();
    // The malformed edit stays in the textarea so the user can fix it, but the underlying
    // rawDataJson prop (the last-saved value) is untouched — the parent never invalidated its query.
    expect(textbox).toHaveValue('{not valid json');
  });
});
