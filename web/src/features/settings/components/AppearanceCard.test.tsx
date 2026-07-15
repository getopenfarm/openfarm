import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { PreferencesProvider } from '@/lib/preferences/usePreferences';
import { AppearanceCard } from './AppearanceCard';

function renderCard() {
  return render(
    <PreferencesProvider>
      <AppearanceCard />
    </PreferencesProvider>,
  );
}

describe('AppearanceCard', () => {
  it('renders the density control', () => {
    renderCard();

    expect(screen.getByRole('radio', { name: 'Comfortable' })).toBeChecked();
  });

  it('updates density and persists it to localStorage', async () => {
    const user = userEvent.setup();
    renderCard();

    await user.click(screen.getByRole('radio', { name: 'Compact' }));
    expect(screen.getByRole('radio', { name: 'Compact' })).toBeChecked();

    const stored = JSON.parse(
      window.localStorage.getItem('openfarm.preferences.v1') ?? '{}',
    );
    expect(stored.density).toBe('compact');
  });
});
