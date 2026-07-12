import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { describe, expect, it } from 'vitest';
import { SettingsPage } from './SettingsPage';

describe('SettingsPage', () => {
  it('links to Connection Secrets', () => {
    render(
      <MemoryRouter>
        <SettingsPage />
      </MemoryRouter>,
    );
    expect(
      screen.getByRole('link', { name: /Connection Secrets/ }),
    ).toHaveAttribute('href', '/settings/connection-secrets');
  });
});
