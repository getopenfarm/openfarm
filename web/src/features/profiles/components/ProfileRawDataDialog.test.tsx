import { render, screen } from '@testing-library/react';
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
    expect(screen.getByText(/nozzle_temperature/)).toBeInTheDocument();
    expect(screen.getByText(/220/)).toBeInTheDocument();
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
});
