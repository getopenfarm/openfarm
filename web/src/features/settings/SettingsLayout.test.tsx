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
            element: <Navigate to="general" replace />,
          },
          { path: 'general', element: <div>General page</div> },
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
});
