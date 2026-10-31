import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
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
            plates: [],
          }}
          overrides={{}}
          onNarrow={vi.fn()}
          plateNumbers={[]}
          onPlateNumbersChange={vi.fn()}
        />
      </QueryClientProvider>,
    );
    expect(
      screen.getByText('Nozzle diameter').nextElementSibling,
    ).toHaveTextContent('Unknown');
    expect(
      screen.getByText('Units per print').nextElementSibling,
    ).toHaveTextContent('Unknown');
    expect(
      screen.getByText('Time unavailable · Weight unavailable'),
    ).toBeInTheDocument();
    expect(
      screen.getByText('No printer profiles configured.'),
    ).toBeInTheDocument();
    expect(screen.getByText(/You can save this part/)).toBeInTheDocument();
    expect(screen.queryByText('Estimating…')).not.toBeInTheDocument();
  });

  it('shows detected units while the print estimate is still resolving', () => {
    render(
      <QueryClientProvider client={new QueryClient()}>
        <PartProcessingReviewSection
          status="ReadyForReview"
          preview={{
            estimateStatus: 'Resolving',
            unitsPerPrint: 4,
            plates: [],
            filamentRequirements: [],
          }}
          overrides={{}}
          onNarrow={vi.fn()}
          plateNumbers={[]}
          onPlateNumbersChange={vi.fn()}
        />
      </QueryClientProvider>,
    );
    expect(
      screen.getByText('Units per print').nextElementSibling,
    ).toHaveTextContent('4');
    expect(screen.getByText('Estimating…')).toBeVisible();
    expect(screen.queryByRole('spinbutton')).not.toBeInTheDocument();
  });

  it('shows the printer model and no estimate basis', () => {
    render(
      <QueryClientProvider client={new QueryClient()}>
        <PartProcessingReviewSection
          status="ReadyForReview"
          preview={{
            nozzleDiameterMm: 0.4,
            estimateStatus: 'Available',
            estimatedPrintTimeMinutes: 8,
            estimatedFilamentWeightGrams: 1.21,
            filamentRequirements: [],
            plates: [],
            estimation: {
              source: 'ProjectPresets',
              machineModel: 'Bambu Lab P1S',
              machineProfileName: 'Bambu Lab P1S 0.4 nozzle',
            },
          }}
          overrides={{}}
          onNarrow={vi.fn()}
          plateNumbers={[]}
          onPlateNumbersChange={vi.fn()}
        />
      </QueryClientProvider>,
    );
    expect(screen.getByText('Printer model')).toBeInTheDocument();
    expect(screen.getByText('Bambu Lab P1S')).toBeInTheDocument();
    expect(screen.queryByText(/Estimate basis/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Production slicing/)).not.toBeInTheDocument();
  });

  it('asks for a plate when the file offers several, then shows only that plate', async () => {
    const preview = {
      nozzleDiameterMm: 0.4,
      estimateStatus: 'Available',
      estimatedPrintTimeMinutes: 40,
      estimatedFilamentWeightGrams: 13,
      filamentRequirements: [
        {
          requirementIndex: 0,
          materialType: 'PLA',
          detectedColorHex: '#FFFFFF',
        },
        {
          requirementIndex: 1,
          materialType: 'PETG',
          detectedColorHex: '#000000',
        },
      ],
      plates: [
        {
          number: 1,
          name: 'Cover',
          unitsPerPrint: 4,
          estimatedPrintTimeMinutes: 10,
          estimatedFilamentWeightGrams: 4,
          filamentRequirements: [
            {
              requirementIndex: 0,
              materialType: 'PLA',
              detectedColorHex: '#FFFFFF',
            },
          ],
        },
        {
          number: 2,
          name: 'Handle',
          unitsPerPrint: 2,
          estimatedPrintTimeMinutes: 30,
          estimatedFilamentWeightGrams: 9,
          filamentRequirements: [
            {
              requirementIndex: 1,
              materialType: 'PETG',
              detectedColorHex: '#000000',
            },
          ],
        },
      ],
    };
    const onPlateNumbersChange = vi.fn();
    const { rerender } = render(
      <QueryClientProvider client={new QueryClient()}>
        <PartProcessingReviewSection
          status="ReadyForReview"
          preview={preview}
          overrides={{}}
          onNarrow={vi.fn()}
          plateNumbers={[]}
          onPlateNumbersChange={onPlateNumbersChange}
        />
      </QueryClientProvider>,
    );

    expect(screen.getByText('Choose a plate.')).toBeInTheDocument();
    await userEvent.click(
      screen.getByRole('radio', { name: /Plate 2 · Handle/ }),
    );
    expect(onPlateNumbersChange).toHaveBeenCalledWith([2]);

    rerender(
      <QueryClientProvider client={new QueryClient()}>
        <PartProcessingReviewSection
          status="ReadyForReview"
          preview={preview}
          overrides={{}}
          onNarrow={vi.fn()}
          plateNumbers={[2]}
          onPlateNumbersChange={onPlateNumbersChange}
        />
      </QueryClientProvider>,
    );
    expect(screen.queryByText('Choose a plate.')).not.toBeInTheDocument();
    const estimate = screen.getByText('Estimated print')
      .parentElement as HTMLElement;
    expect(within(estimate).getByText('30m · 9 g')).toBeInTheDocument();
    expect(screen.getByText(/PETG/)).toBeInTheDocument();
    expect(
      screen.getByText('Units per print').nextElementSibling,
    ).toHaveTextContent('2');
    expect(screen.getByText('4 units per print')).toBeInTheDocument();
    expect(screen.queryByText(/PLA/)).not.toBeInTheDocument();
  });

  it('takes several plates as checkbox tiles and narrows their filaments together', async () => {
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
    const pla = (estimatedGrams: number) => ({
      requirementIndex: 0,
      materialType: 'PLA',
      detectedColorHex: '#FFFFFF',
      estimatedGrams,
    });
    const onPlateNumbersChange = vi.fn();
    render(
      <QueryClientProvider client={new QueryClient()}>
        <PartProcessingReviewSection
          status="ReadyForReview"
          preview={{
            estimateStatus: 'Available',
            filamentRequirements: [],
            plates: [
              {
                number: 1,
                name: 'Cover',
                unitsPerPrint: 4,
                estimatedFilamentWeightGrams: 4,
                filamentRequirements: [pla(3)],
              },
              {
                number: 2,
                name: 'Handle',
                unitsPerPrint: 2,
                estimatedPrintTimeMinutes: 30,
                estimatedFilamentWeightGrams: 9,
                filamentRequirements: [
                  pla(5),
                  {
                    requirementIndex: 1,
                    materialType: 'PETG',
                    detectedColorHex: '#000000',
                  },
                ],
              },
            ],
          }}
          overrides={{}}
          onNarrow={vi.fn()}
          plateNumbers={[1, 2]}
          onPlateNumbersChange={onPlateNumbersChange}
          multiple
        />
      </QueryClientProvider>,
    );

    const cover = screen.getByRole('checkbox', { name: /Plate 1 · Cover/ });
    expect(cover).toBeChecked();
    expect(cover.closest('label')).toHaveTextContent(
      'Plate 1 · Cover4 units per print',
    );
    expect(screen.getByText('2 units per print')).toBeVisible();
    expect(screen.queryByText(/unavailable/)).not.toBeInTheDocument();

    // Each filament once, its grams summed over the checked plates.
    expect(screen.getAllByText(/PLA/)).toHaveLength(1);
    expect(screen.getByText('8 g')).toBeInTheDocument();
    expect(screen.getByText(/PETG/)).toBeInTheDocument();

    await userEvent.click(
      screen.getByRole('checkbox', { name: /Plate 2 · Handle/ }),
    );
    expect(onPlateNumbersChange).toHaveBeenLastCalledWith([1]);
    await userEvent.click(screen.getByRole('checkbox', { name: 'All plates' }));
    expect(onPlateNumbersChange).toHaveBeenLastCalledWith([]);
  });
});
