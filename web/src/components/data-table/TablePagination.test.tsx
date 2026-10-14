import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { TablePagination } from './TablePagination';

const defaults = {
  page: 1,
  pageSize: 10,
  totalCount: 15,
  onPageChange: vi.fn(),
};
describe('TablePagination', () => {
  it('matches the range and numbered controls in the reference', async () => {
    const change = vi.fn();
    render(<TablePagination {...defaults} onPageChange={change} />);
    expect(screen.getByText('1–10 of 15')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Page 1' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    expect(
      screen.getByRole('button', { name: 'Previous table page' }),
    ).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: 'Page 2' }));
    expect(change).toHaveBeenCalledWith(2);
  });
  it('uses a bounded page window with ellipses for large collections', () => {
    render(<TablePagination {...defaults} page={50} totalCount={1000} />);
    expect(screen.getByText('491–500 of 1000')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Page 100' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Page 50' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    expect(
      screen.queryByRole('button', { name: 'Page 20' }),
    ).not.toBeInTheDocument();
    expect(screen.getAllByText('More pages')).toHaveLength(2);
  });
  it('handles empty collections and pending controls', () => {
    const { rerender } = render(
      <TablePagination {...defaults} totalCount={0} />,
    );
    expect(screen.getByText('0–0 of 0')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Next table page' }),
    ).toBeDisabled();
    rerender(
      <TablePagination {...defaults} disabled onPageSizeChange={vi.fn()} />,
    );
    expect(
      screen.getByRole('combobox', { name: 'Rows per page' }),
    ).toBeDisabled();
    for (const button of screen.getAllByRole('button'))
      expect(button).toBeDisabled();
  });
  it('offers the four supported page sizes', async () => {
    const change = vi.fn();
    render(<TablePagination {...defaults} onPageSizeChange={change} />);
    const user = userEvent.setup();
    await user.click(screen.getByRole('combobox', { name: 'Rows per page' }));
    expect(screen.getAllByRole('option')).toHaveLength(4);
    await user.click(screen.getByRole('option', { name: '100' }));
    expect(change).toHaveBeenCalledWith(100);
  });
});
