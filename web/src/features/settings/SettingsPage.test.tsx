import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { describe, expect, it } from 'vitest';
import { PreferencesProvider } from '@/lib/preferences/usePreferences';
import { SettingsPage } from './SettingsPage';

describe('SettingsPage', () => {
  it('links to Connection Secrets', () => {
    render(
      <PreferencesProvider>
        <MemoryRouter>
          <SettingsPage />
        </MemoryRouter>
      </PreferencesProvider>,
    );
    expect(
      screen.getByRole('link', { name: /Connection Secrets/ }),
    ).toHaveAttribute('href', '/settings/connection-secrets');
  });

  it('links to Slicing Defaults', () => {
    render(
      <PreferencesProvider>
        <MemoryRouter>
          <SettingsPage />
        </MemoryRouter>
      </PreferencesProvider>,
    );
    expect(
      screen.getByRole('link', { name: /Slicing Defaults/ }),
    ).toHaveAttribute('href', '/settings/slicing-defaults');
  });
});
