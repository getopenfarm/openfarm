import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { PartProcessingReviewSection } from './PartProcessingReviewSection';

describe('PartProcessingReviewSection', () => {
  afterEach(() => vi.unstubAllGlobals());
  it('renders a reviewable preview with missing print metadata and no pending estimate', () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() =>
        Promise.resolve(
          new Response(JSON.stringify({ filaments: [] }), {
            headers: { 'Content-Type': 'application/json' },
          }),
        ),
      ),
    );
    render(
      <QueryClientProvider client={new QueryClient()}>
        <PartProcessingReviewSection
          status="ReadyForReview"
          preview={{
            nozzleDiameterMm: null,
            estimateStatus: 'Unavailable',
            printRequirementsStatus: 'Incomplete',
            filamentRequirements: [],
            processingWarnings: ['No printer profiles configured.'],
            plateCount: 1,
          }}
          overrides={{}}
          onNarrow={vi.fn()}
        />
      </QueryClientProvider>,
    );
    expect(screen.getByText('Unknown')).toBeInTheDocument();
    expect(
      screen.getByText('Time unavailable · Weight unavailable'),
    ).toBeInTheDocument();
    expect(
      screen.getByText('No printer profiles configured.'),
    ).toBeInTheDocument();
    expect(screen.getByText(/You can save this part/)).toBeInTheDocument();
    expect(screen.queryByText('Estimating…')).not.toBeInTheDocument();
  });
});
