import { render, screen } from '@testing-library/react';
import { createMemoryRouter, Navigate, RouterProvider } from 'react-router';
import { describe, expect, it } from 'vitest';
import { AccessContext } from '@/lib/auth/AccessContext';
import { SettingsLayout } from './SettingsLayout';

function renderAt(path: string, access?: ReadonlySet<string>) {
  const router = createMemoryRouter(
    [
      {
        path: '/settings',
        element: <SettingsLayout />,
        children: [
          {
            index: true,
            element: <Navigate to="general" replace />,
          },
          {
            path: 'general',
            element: (
              <div>
                General page <input aria-label="Farm name" />
              </div>
            ),
          },
          { path: 'slicing-defaults', element: <div>Slicing page</div> },
        ],
      },
    ],
    { initialEntries: [path] },
  );
  return render(
    <AccessContext.Provider value={access}>
      <RouterProvider router={router} />
    </AccessContext.Provider>,
  );
}

describe('SettingsLayout', () => {
  it('links to each section', () => {
    renderAt('/settings/slicing-defaults');

    expect(
      screen.queryByRole('link', { name: 'Connection credentials' }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole('link', { name: 'Slicing defaults' }),
    ).toHaveAttribute('href', '/settings/slicing-defaults');
    expect(screen.getByRole('link', { name: 'Printer costs' })).toHaveAttribute(
      'href',
      '/settings/printer-costs',
    );
    expect(screen.getByText('Slicing page')).toBeInTheDocument();
  });

  it('redirects the index to General', () => {
    renderAt('/settings');

    expect(screen.getByText('General page')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'General' })).toHaveAttribute(
      'aria-current',
      'page',
    );
  });

  it('shows a read-only note and disables controls without the update key', () => {
    renderAt('/settings/general', new Set(['settings:general:read']));

    expect(
      screen.getByText(/Settings › General › Change permission/),
    ).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Farm name' })).toBeDisabled();
  });

  it('keeps controls enabled with the update key', () => {
    renderAt(
      '/settings/general',
      new Set(['settings:general:read', 'settings:general:update']),
    );

    expect(screen.queryByText(/not change them/)).not.toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Farm name' })).toBeEnabled();
  });
});
