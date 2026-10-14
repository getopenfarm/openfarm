import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { Link, MemoryRouter, Route, Routes } from 'react-router';
import { describe, expect, it, vi } from 'vitest';
import { DataTable } from './DataTable';

describe('DataTable navigation', () => {
  it('allows keyboard link navigation and keeps nested actions separate from row clicks', async () => {
    const onRowClick = vi.fn();
    const action = vi.fn();
    render(
      <MemoryRouter>
        <Routes>
          <Route
            path="/"
            element={
              <DataTable
                data={[{ id: 'printer', name: 'Printer one' }]}
                onRowClick={onRowClick}
                columns={[
                  {
                    id: 'name',
                    header: 'Printer',
                    cell: ({ row }) => (
                      <Link to={`/printers/${row.original.id}`}>
                        {row.original.name}
                      </Link>
                    ),
                  },
                  {
                    id: 'action',
                    header: 'Action',
                    cell: () => (
                      <button type="button" onClick={action}>
                        Mark ready
                      </button>
                    ),
                  },
                  { id: 'status', header: 'Status', cell: () => 'Idle' },
                ]}
              />
            }
          />
          <Route path="/printers/:id" element={<h1>Printer detail</h1>} />
        </Routes>
      </MemoryRouter>,
    );
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Mark ready' }));
    expect(action).toHaveBeenCalledOnce();
    expect(onRowClick).not.toHaveBeenCalled();
    await user.click(screen.getByText('Idle'));
    expect(onRowClick).toHaveBeenCalledOnce();
    onRowClick.mockClear();
    screen.getByRole('link', { name: 'Printer one' }).focus();
    await user.keyboard('{Enter}');
    expect(
      screen.getByRole('heading', { name: 'Printer detail' }),
    ).toBeInTheDocument();
    expect(onRowClick).not.toHaveBeenCalled();
  });
});

describe('DataTable pagination', () => {
  it('changes local page size and returns to page one', async () => {
    render(
      <DataTable
        pageSize={25}
        columns={[{ accessorKey: 'name', header: 'Name' }]}
        data={Array.from({ length: 30 }, (_, i) => ({
          name: `Record ${i + 1}`,
        }))}
      />,
    );
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Next table page' }));
    expect(screen.getByText('Record 26')).toBeInTheDocument();
    await user.click(screen.getByRole('combobox', { name: 'Rows per page' }));
    await user.click(screen.getByRole('option', { name: '10' }));
    expect(screen.getByText('1–10 of 30')).toBeInTheDocument();
    expect(screen.getByText('Record 1')).toBeInTheDocument();
    expect(screen.queryByText('Record 26')).not.toBeInTheDocument();
  });

  const columns = [{ accessorKey: 'name', header: 'Name' }];
  const data = Array.from({ length: 26 }, (_, index) => ({
    name: `Item ${index + 1}`,
  }));

  it('limits local rows, navigates pages, and resets when the collection is filtered', async () => {
    const user = userEvent.setup();
    const { rerender } = render(
      <DataTable pageSize={25} columns={columns} data={data} />,
    );
    expect(screen.getByText('Item 1')).toBeInTheDocument();
    expect(screen.queryByText('Item 26')).not.toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Previous table page' }),
    ).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Next table page' }));
    expect(screen.getByText('Item 26')).toBeInTheDocument();
    expect(screen.queryByText('Item 1')).not.toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Next table page' }),
    ).toBeDisabled();
    rerender(
      <DataTable pageSize={25} columns={columns} data={data.slice(0, 2)} />,
    );
    expect(await screen.findByText('Item 1')).toBeInTheDocument();
    expect(screen.getByText('1–2 of 2')).toBeInTheDocument();
  });

  it('requests server pages without slicing the returned page again and recovers after removal', async () => {
    const requestPage = vi.fn();
    function ServerList({ totalCount = 26, pending = false }) {
      const [page, setPage] = useState(1);
      return (
        <DataTable
          columns={columns}
          data={
            page === 1
              ? data.slice(0, 25)
              : totalCount > 25
                ? data.slice(25)
                : []
          }
          serverPagination={{
            page,
            pageSize: 25,
            totalCount,
            pending,
            onPageChange: (next) => {
              requestPage(next);
              setPage(next);
            },
          }}
        />
      );
    }
    const user = userEvent.setup();
    const { rerender } = render(<ServerList pending />);
    expect(
      screen.getByRole('button', { name: 'Next table page' }),
    ).toBeDisabled();
    rerender(<ServerList />);
    await user.click(screen.getByRole('button', { name: 'Next table page' }));
    expect(requestPage).toHaveBeenCalledWith(2);
    expect(screen.getByText('Item 26')).toBeInTheDocument();
    expect(screen.getByText('26–26 of 26')).toBeInTheDocument();
    rerender(<ServerList totalCount={25} />);
    expect(await screen.findByText('Item 1')).toBeInTheDocument();
    expect(requestPage).toHaveBeenLastCalledWith(1);
  });
});

describe('DataTable sorting', () => {
  const data = [
    { id: 'z', name: 'Zulu' },
    { id: 'a', name: 'Alpha' },
  ];

  it('uses keyboard server sort controls without reordering the supplied server page', async () => {
    const { DataTableColumnHeader } = await import('./DataTableColumnHeader');
    const { useServerListState } = await import('./useServerListState');
    const columns: import('@tanstack/react-table').ColumnDef<
      (typeof data)[number]
    >[] = [
      {
        accessorKey: 'name',
        header: ({ column }) => (
          <DataTableColumnHeader column={column} title="Name" />
        ),
      },
    ];
    function Harness() {
      const list = useServerListState('name', 'name');
      return (
        <DataTable
          data={data}
          serverPagination={{
            page: list.page,
            pageSize: 25,
            totalCount: 100,
            onPageChange: list.setPage,
          }}
          serverSorting={list.serverSorting}
          columns={columns}
        />
      );
    }
    render(<Harness />);
    const user = userEvent.setup();
    const header = screen.getByRole('button', { name: 'Name' });
    header.focus();
    await user.keyboard('{Enter}');
    expect(header.closest('th')).toHaveAttribute('aria-sort', 'ascending');
    expect(screen.getAllByRole('cell').map((cell) => cell.textContent)).toEqual(
      ['Zulu', 'Alpha'],
    );
    expect(header).toHaveFocus();
    await user.keyboard(' ');
    expect(header.closest('th')).toHaveAttribute('aria-sort', 'descending');
    await user.keyboard('{Enter}');
    expect(header.closest('th')).not.toHaveAttribute('aria-sort');
  });

  it('does not offer sorting for server pagination without a server sorting handler', async () => {
    const { DataTableColumnHeader } = await import('./DataTableColumnHeader');
    render(
      <DataTable
        data={data}
        serverPagination={{
          page: 1,
          pageSize: 25,
          totalCount: 100,
          onPageChange: vi.fn(),
        }}
        columns={[
          {
            accessorKey: 'name',
            header: ({ column }) => (
              <DataTableColumnHeader column={column} title="Name" />
            ),
          },
        ]}
      />,
    );
    expect(
      screen.queryByRole('button', { name: 'Name' }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole('columnheader', { name: 'Name' }),
    ).toBeInTheDocument();
  });

  it('retains local sorting for complete collections', async () => {
    const { DataTableColumnHeader } = await import('./DataTableColumnHeader');
    render(
      <DataTable
        data={data}
        pageSize={25}
        columns={[
          {
            accessorKey: 'name',
            header: ({ column }) => (
              <DataTableColumnHeader column={column} title="Name" />
            ),
          },
        ]}
      />,
    );
    await userEvent.click(screen.getByRole('button', { name: 'Name' }));
    expect(screen.getAllByRole('cell').map((cell) => cell.textContent)).toEqual(
      ['Alpha', 'Zulu'],
    );
  });
});
