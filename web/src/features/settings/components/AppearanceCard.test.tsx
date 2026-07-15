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
  it('renders sidebar layout and density controls', () => {
    renderCard();

    expect(screen.getByRole('radio', { name: 'Grouped' })).toBeChecked();
    expect(screen.getByRole('radio', { name: 'Comfortable' })).toBeChecked();
  });

  it('updates preferences and persists them to localStorage', async () => {
    const user = userEvent.setup();
    renderCard();

    await user.click(screen.getByRole('radio', { name: 'Compact' }));
    expect(screen.getByRole('radio', { name: 'Compact' })).toBeChecked();

    await user.click(screen.getByRole('radio', { name: 'Rail' }));
    expect(screen.getByRole('radio', { name: 'Rail' })).toBeChecked();

    const stored = JSON.parse(
      window.localStorage.getItem('openfarm.preferences.v1') ?? '{}',
    );
    expect(stored.density).toBe('compact');
    expect(stored.sidebarVariant).toBe('rail');
  });
});
