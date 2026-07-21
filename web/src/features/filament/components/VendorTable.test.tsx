import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { VendorTable } from './VendorTable';

describe('VendorTable', () => {
  it('lists vendors', () => {
    render(
      <VendorTable
        vendors={[
          { id: 'v-1', name: 'Bambu Lab' },
          { id: 'v-2', name: 'Polymaker' },
        ]}
      />,
    );

    expect(screen.getByText('Bambu Lab')).toBeInTheDocument();
    expect(screen.getByText('Polymaker')).toBeInTheDocument();
  });
});
