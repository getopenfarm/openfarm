import type { SortingState } from '@tanstack/react-table';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { Tabs, TabsContent } from '@/components/ui/tabs';
import type { ViewMode } from '@/lib/preferences/types';
import { LibraryViewControls } from './LibraryViewControls';

function Example({ disabled = false }: { disabled?: boolean }) {
  const [view, setView] = useState<ViewMode>('table');
  const [sorting, setSorting] = useState<SortingState>([]);
  return (
    <Tabs value={view} onValueChange={(value) => setView(value as ViewMode)}>
      <LibraryViewControls
        view={view}
        sorting={{ sorting, onSortingChange: setSorting }}
        columnId="name"
        disabled={disabled}
      />
      <TabsContent value="table">Details content</TabsContent>
      <TabsContent value="grid">Thumbnail content</TabsContent>
    </Tabs>
  );
}

describe('Library view controls', () => {
  it('retains active tab styling attributes through clicks and tooltip hover', async () => {
    const user = userEvent.setup();
    render(<Example />);
    const details = screen.getByRole('tab', { name: 'Details view' });
    const thumbnails = screen.getByRole('tab', { name: 'Thumbnails view' });
    expect(details).toHaveAttribute('data-state', 'active');
    expect(details).toHaveAttribute('data-slot', 'tabs-trigger');
    expect(thumbnails).toHaveAttribute('data-state', 'inactive');
    await user.click(thumbnails);
    expect(thumbnails).toHaveAttribute('aria-selected', 'true');
    expect(thumbnails).toHaveAttribute('data-state', 'active');
    expect(details).toHaveAttribute('data-state', 'inactive');
    expect(screen.getByRole('tabpanel')).toHaveTextContent('Thumbnail content');
    expect(
      screen.getByRole('combobox', { name: 'Sort items' }),
    ).toHaveTextContent('Newest first');
    await user.hover(details);
    expect(details).toHaveAttribute('data-state', 'inactive');
    expect(thumbnails).toHaveAttribute('data-state', 'active');
    await user.unhover(details);
    expect(details).toHaveAttribute('data-state', 'inactive');
  });

  it('switches views with arrow keys and keeps the sort control separate from the tab order', async () => {
    const user = userEvent.setup();
    render(<Example />);
    await user.tab();
    expect(screen.getByRole('tab', { name: 'Details view' })).toHaveFocus();
    await user.keyboard('{ArrowRight}');
    const thumbnails = screen.getByRole('tab', { name: 'Thumbnails view' });
    expect(thumbnails).toHaveFocus();
    expect(thumbnails).toHaveAttribute('data-state', 'active');
    await user.keyboard('{ArrowLeft}');
    const details = screen.getByRole('tab', { name: 'Details view' });
    expect(details).toHaveFocus();
    expect(details).toHaveAttribute('data-state', 'active');
    expect(
      screen.queryByRole('combobox', { name: 'Sort items' }),
    ).not.toBeInTheDocument();
  });

  it('disables view switching while a conflicting operation is pending', () => {
    render(<Example disabled />);
    expect(screen.getByRole('tab', { name: 'Details view' })).toBeDisabled();
    expect(screen.getByRole('tab', { name: 'Thumbnails view' })).toBeDisabled();
  });
});
