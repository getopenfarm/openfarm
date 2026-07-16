import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { profilesStore } from './data/mockProfilesStore';
import { ProfilesPage } from './ProfilesPage';

describe('ProfilesPage', () => {
  beforeEach(() => {
    profilesStore.reset();
  });

  it('renders both category badges and profile names', () => {
    render(<ProfilesPage />);

    expect(screen.getByText('Profiles')).toBeInTheDocument();
    expect(screen.getAllByText('Machine').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Filament').length).toBeGreaterThan(0);

    expect(screen.getByText('Bambu X1 Carbon 0.4 nozzle')).toBeInTheDocument();
    expect(
      screen.getAllByText('Bambu PLA Basic @ Bambu X1 Carbon').length,
    ).toBeGreaterThan(0);
  });

  it('shows the profile count in the subtitle', () => {
    const total = profilesStore.getSnapshot().length;
    render(<ProfilesPage />);

    expect(screen.getByText(`${total} slicer profiles`)).toBeInTheDocument();
  });
});
