import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { PartEstimationSource } from './PartEstimationSource';

describe('PartEstimationSource', () => {
  it('shows embedded custom profiles and distinguishes library estimates from production', () => {
    render(
      <PartEstimationSource
        estimation={{
          source: 'EmbeddedProject',
          machineModel: 'Bambu Lab P1S',
          machineProfileName: 'Custom Scrubbler',
          processProfileName: '0.30mm Standard',
          filamentProfileNames: ['Aliz PETG'],
          engine: 'OrcaSlicer',
          version: '2.3.1',
        }}
      />,
    );
    expect(
      screen.getByText('Estimate basis: Embedded project settings'),
    ).toBeInTheDocument();
    expect(screen.getByText(/Custom Scrubbler/)).toBeInTheDocument();
    expect(
      screen.getByText(/Production slicing uses the assigned printer/),
    ).toBeInTheDocument();
  });
  it('labels estimates from defaults', () => {
    render(<PartEstimationSource estimation={{ source: 'DefaultProfiles' }} />);
    expect(
      screen.getByText('Estimate basis: Default estimation profiles'),
    ).toBeInTheDocument();
  });
  it('labels sliced metadata without claiming that its G-code will be sliced again', () => {
    render(
      <PartEstimationSource
        estimation={{ source: 'EmbeddedSlicedMetadata' }}
      />,
    );
    expect(
      screen.getByText('Estimate basis: Embedded sliced metadata'),
    ).toBeInTheDocument();
    expect(screen.queryByText(/Production slicing/)).not.toBeInTheDocument();
  });
});
