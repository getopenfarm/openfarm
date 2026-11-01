import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Button } from './button';
import { Spinner } from './spinner';

describe('Spinner accessibility', () => {
  it('announces standalone loading without adding a second label to pending actions', () => {
    render(
      <>
        <Spinner />
        <Button disabled>
          <Spinner data-icon="inline-start" />
          Saving…
        </Button>
      </>,
    );
    expect(screen.getByRole('status', { name: 'Loading' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Saving…' })).toBeDisabled();
    expect(screen.getAllByRole('status')).toHaveLength(1);
  });
});
