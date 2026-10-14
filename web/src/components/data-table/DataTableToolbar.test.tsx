import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { Tabs, TabsContent, TabsTrigger } from '@/components/ui/tabs';
import { DataTableSearch } from './DataTableSearch';
import { DataTableTabsList, DataTableToolbar } from './DataTableToolbar';

function SearchExample() {
  const [value, setValue] = useState('');
  return (
    <DataTableToolbar
      search={
        <DataTableSearch
          label="Search printers"
          placeholder="Search printers or models…"
          value={value}
          onValueChange={setValue}
        />
      }
    >
      <button type="button">All printers</button>
    </DataTableToolbar>
  );
}

describe('Table controls', () => {
  it('provides a named controlled search after the filters in keyboard order', async () => {
    const user = userEvent.setup();
    render(<SearchExample />);
    await user.tab();
    expect(screen.getByRole('button', { name: 'All printers' })).toHaveFocus();
    await user.tab();
    const search = screen.getByRole('textbox', { name: 'Search printers' });
    expect(search).toHaveFocus();
    await user.type(search, 'P1S');
    expect(search).toHaveValue('P1S');
    await user.clear(search);
    expect(search).toHaveValue('');
  });

  it('preserves arrow-key activation and panel associations inside the toolbar', async () => {
    const user = userEvent.setup();
    render(
      <Tabs defaultValue="machine">
        <DataTableToolbar>
          <DataTableTabsList aria-label="Profile type">
            <TabsTrigger value="machine">Machine</TabsTrigger>
            <TabsTrigger value="filament">Filament</TabsTrigger>
          </DataTableTabsList>
        </DataTableToolbar>
        <TabsContent value="machine">Machine profiles</TabsContent>
        <TabsContent value="filament">Filament profiles</TabsContent>
      </Tabs>,
    );
    await user.tab();
    expect(screen.getByRole('tab', { name: 'Machine' })).toHaveFocus();
    await user.keyboard('{ArrowRight}');
    expect(screen.getByRole('tab', { name: 'Filament' })).toHaveFocus();
    expect(screen.getByRole('tab', { name: 'Filament' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    expect(
      screen.getByRole('tabpanel', { name: 'Filament' }),
    ).toHaveTextContent('Filament profiles');
  });
});
