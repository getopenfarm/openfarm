import { render, screen } from '@testing-library/react';
import { createMemoryRouter, Navigate, RouterProvider } from 'react-router';
import { describe, expect, it } from 'vitest';
import { SettingsLayout } from './SettingsLayout';

function renderAt(path: string) {
  const router = createMemoryRouter(
    [
      {
        path: '/settings',
        element: <SettingsLayout />,
        children: [
          {
            index: true,
            element: <Navigate to="connection-secrets" replace />,
          },
          { path: 'connection-secrets', element: <div>Credentials page</div> },
          { path: 'slicing-defaults', element: <div>Slicing page</div> },
        ],
      },
    ],
    { initialEntries: [path] },
  );
  return render(<RouterProvider router={router} />);
}

describe('SettingsLayout', () => {
  it('links to each section', () => {
    renderAt('/settings/slicing-defaults');

    expect(
      screen.getByRole('link', { name: 'Connection credentials' }),
    ).toHaveAttribute('href', '/settings/connection-secrets');
    expect(
      screen.getByRole('link', { name: 'Slicing defaults' }),
    ).toHaveAttribute('href', '/settings/slicing-defaults');
    expect(screen.getByText('Slicing page')).toBeInTheDocument();
  });

  it('redirects the index to Connection credentials', () => {
    renderAt('/settings');

    expect(screen.getByText('Credentials page')).toBeInTheDocument();
    expect(
      screen.getByRole('link', { name: 'Connection credentials' }),
    ).toHaveAttribute('aria-current', 'page');
  });
});
