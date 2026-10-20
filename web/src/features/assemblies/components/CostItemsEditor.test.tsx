import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { AssemblyCostInput } from '@/lib/api/generated/endpoints.schemas';
import { CostItemsEditor } from './CostItemsEditor';

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
const item = (
  id: string,
  name: string,
  category: string,
  unit: string,
  effectiveRate: number | null,
) => ({
  id,
  name,
  effectiveRate,
  pricing: {
    itemType: unit === 'Hour' ? 'Service' : 'Physical',
    category,
    unit,
  },
});
const catalog = [
  item('wrap', 'Bubble wrap', 'Packaging', 'SquareMetre', 0.4),
  item('bag', 'Courier bag', 'Packaging', 'Piece', 0.3),
  item('labor', 'Assembly labor', 'Labour', 'Hour', 24),
  item('screw', 'M3 screw', 'Hardware', 'Piece', 0.05),
  item('tape', 'Tape', 'Consumables', 'Metre', 0.06),
  item('glue', 'Mystery glue', 'Consumables', 'Millilitre', null),
];

function stubCatalog() {
  vi.stubGlobal(
    'fetch',
    vi.fn(() =>
      Promise.resolve(json({ items: catalog, totalCount: catalog.length })),
    ),
  );
}

function Harness({
  initial = [],
  structureItemIds = [],
  onFormSubmit,
}: {
  initial?: AssemblyCostInput[];
  structureItemIds?: string[];
  onFormSubmit?: () => void;
}) {
  const [lines, setLines] = useState<AssemblyCostInput[]>(initial);
  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        onFormSubmit?.();
      }}
    >
      <CostItemsEditor
        lines={lines}
        onChange={setLines}
        structureItemIds={structureItemIds}
      />
      <output aria-label="Saved draft">{JSON.stringify(lines)}</output>
      <button type="submit">Save</button>
    </form>
  );
}

function renderEditor(props: Parameters<typeof Harness>[0] = {}) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  render(
    <QueryClientProvider client={client}>
      <Harness {...props} />
    </QueryClientProvider>,
  );
  return userEvent.setup();
}

const draft = () => screen.getByLabelText('Saved draft').textContent ?? '';
const screwLine: AssemblyCostInput = {
  catalogItemId: 'screw',
  purpose: 'Production',
  inputMode: 'Quantity',
  quantity: 4,
  pieces: 1,
};

async function openPicker(user: ReturnType<typeof userEvent.setup>) {
  const add = await screen.findByRole('button', { name: 'Add cost item' });
  await waitFor(() => expect(add).toBeEnabled());
  await user.click(add);
}
async function pick(user: ReturnType<typeof userEvent.setup>, name: RegExp) {
  await openPicker(user);
  await user.click(await screen.findByRole('option', { name }));
}
async function choose(
  user: ReturnType<typeof userEvent.setup>,
  label: string,
  option: string,
) {
  await user.click(screen.getByRole('combobox', { name: label }));
  await user.click(await screen.findByRole('option', { name: option }));
}

afterEach(() => vi.unstubAllGlobals());

describe('CostItemsEditor', () => {
  it('shows an empty state until an item is added', async () => {
    stubCatalog();
    renderEditor();
    expect(await screen.findByText('No cost items')).toBeInTheDocument();
    expect(screen.getByText('0 items')).toBeInTheDocument();
  });

  it('lists catalog items with category and cost, sorted by name', async () => {
    stubCatalog();
    const user = renderEditor();
    await openPicker(user);
    const options = await screen.findAllByRole('option');
    expect(options.map((option) => option.textContent)).toEqual([
      'Assembly laborLabor · 24.00 / hour',
      'Bubble wrapPackaging · 0.40 / m²',
      'Courier bagPackaging · 0.30 / piece',
      'M3 screwHardware · 0.05 / piece',
      'Mystery glueConsumables · Not priced',
      'TapeConsumables · 0.06 / m',
    ]);
  });

  it('shows the item icon, or the category default, in the picker and on rows', async () => {
    const glue = {
      ...item('glue', 'Mystery glue', 'Consumables', 'Millilitre', 0.1),
      icon: 'tube-lotion',
    };
    const items = [glue, item('screw', 'M3 screw', 'Hardware', 'Piece', 0.05)];
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(json({ items, totalCount: items.length }))),
    );
    const user = renderEditor();
    await openPicker(user);
    const glueOption = await screen.findByRole('option', {
      name: /Mystery glue/,
    });
    expect(glueOption.querySelector('.lucide-tube-lotion')).toBeInTheDocument();
    expect(
      screen
        .getByRole('option', { name: /M3 screw/ })
        .querySelector('.lucide-screw'),
    ).toBeInTheDocument();
    await user.click(glueOption);
    const row = (
      await screen.findByLabelText('Quantity for Mystery glue')
    ).closest('li') as HTMLElement;
    expect(row.querySelector('.lucide-tube-lotion')).toBeInTheDocument();
  });

  it('adds a counted item with quantity 1, shows its purpose, and focuses usage', async () => {
    stubCatalog();
    const user = renderEditor();
    await pick(user, /M3 screw/);
    expect(draft()).toContain('"quantity":1');
    expect(draft()).toContain('"purpose":"Production"');
    expect(screen.getByText('Hardware · Production')).toBeInTheDocument();
    expect(screen.getByLabelText('Quantity for M3 screw')).toHaveFocus();
    expect(screen.getByText('0.05')).toBeInTheDocument();
    // The default is selected, so typing replaces it instead of making "14".
    await user.keyboard('4');
    expect(screen.getByLabelText('Quantity for M3 screw')).toHaveValue(4);
    expect(draft()).toContain('"quantity":4');
  });

  it('defaults packaging to final packing and delivery', async () => {
    stubCatalog();
    const user = renderEditor();
    await pick(user, /Courier bag/);
    expect(
      screen.getByText('Packaging · Final packing and delivery'),
    ).toBeInTheDocument();
    expect(draft()).toContain('"purpose":"FinalProduct"');
  });

  it('starts measured and hourly usage blank', async () => {
    stubCatalog();
    const user = renderEditor();
    await pick(user, /Tape/);
    await pick(user, /Assembly labor/);
    expect(screen.getByLabelText('Quantity for Tape')).toHaveValue(null);
    expect(screen.getByLabelText('Minutes for Assembly labor')).toHaveValue(
      null,
    );
    expect(draft()).toContain('"quantity":0');
  });

  it('keeps typed decimals and updates the row cost', async () => {
    stubCatalog();
    const user = renderEditor();
    await pick(user, /Tape/);
    await user.type(screen.getByLabelText('Quantity for Tape'), '0.5');
    expect(screen.getByLabelText('Quantity for Tape')).toHaveValue(0.5);
    expect(draft()).toContain('"quantity":0.5');
    expect(screen.getByText('0.03')).toBeInTheDocument();
  });

  it('opens the dialog for area items and saves a cut', async () => {
    stubCatalog();
    const user = renderEditor();
    await pick(user, /Bubble wrap/);
    expect(
      await screen.findByRole('dialog', { name: 'Edit Bubble wrap' }),
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.getByLabelText('Area (m²)')).toHaveFocus(),
    );
    await choose(user, 'Usage method', 'Length × width');
    await user.type(screen.getByLabelText('Length'), '50');
    await user.type(screen.getByLabelText('Width'), '20');
    expect(
      screen.getByText('0.1 m² × 0.40 = 0.04 per product'),
    ).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Update cost item' }));
    await waitFor(() =>
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument(),
    );
    expect(draft()).toContain('"inputMode":"Dimensions"');
    expect(draft()).toContain('"length":50');
    expect(draft()).toContain('"width":20');
    expect(draft()).toContain('"pieces":1');
    expect(screen.getByText('50 × 20 cm')).toBeInTheDocument();
    expect(screen.getByText('0.04')).toBeInTheDocument();
  });

  it('blocks Update until usage is valid', async () => {
    stubCatalog();
    const user = renderEditor();
    await pick(user, /Bubble wrap/);
    const update = await screen.findByRole('button', {
      name: 'Update cost item',
    });
    expect(update).toBeDisabled();
    await user.type(screen.getByLabelText('Area (m²)'), '0.5');
    expect(update).toBeEnabled();
  });

  it('edits details with the pencil and leaves the draft on Cancel', async () => {
    stubCatalog();
    const user = renderEditor({ initial: [screwLine] });
    await screen.findByText('Hardware · Production');
    await user.click(screen.getByRole('button', { name: 'Edit M3 screw' }));
    await waitFor(() =>
      expect(screen.getByLabelText('Quantity (piece)')).toHaveFocus(),
    );
    await user.clear(screen.getByLabelText('Quantity (piece)'));
    await user.type(screen.getByLabelText('Quantity (piece)'), '9');
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    await waitFor(() =>
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument(),
    );
    expect(draft()).toContain('"quantity":4');

    await user.click(screen.getByRole('button', { name: 'Edit M3 screw' }));
    await choose(user, 'Counts as', 'Final packing and delivery');
    await user.click(screen.getByRole('button', { name: 'Update cost item' }));
    await waitFor(() =>
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument(),
    );
    expect(draft()).toContain('"purpose":"FinalProduct"');
    expect(
      screen.getByText('Hardware · Final packing and delivery'),
    ).toBeInTheDocument();
  });

  it('disables items already added or still in Structure', async () => {
    stubCatalog();
    const user = renderEditor({
      initial: [screwLine],
      structureItemIds: ['labor'],
    });
    await screen.findByText('Hardware · Production');
    await openPicker(user);
    const added = await screen.findByRole('option', { name: /M3 screw/ });
    expect(added).toHaveAttribute('aria-disabled', 'true');
    expect(added).toHaveTextContent('Added');
    const inStructure = screen.getByRole('option', { name: /Assembly labor/ });
    expect(inStructure).toHaveAttribute('aria-disabled', 'true');
    expect(inStructure).toHaveTextContent('In Structure');
  });

  it('removes a row and returns focus to Add', async () => {
    stubCatalog();
    const user = renderEditor({ initial: [screwLine] });
    await screen.findByText('Hardware · Production');
    await user.click(screen.getByRole('button', { name: 'Remove M3 screw' }));
    expect(screen.getByLabelText('Saved draft')).toHaveTextContent('[]');
    expect(screen.getByRole('button', { name: 'Add cost item' })).toHaveFocus();
  });

  it('shows Not priced for an unpriced item', async () => {
    stubCatalog();
    renderEditor({
      initial: [{ ...screwLine, catalogItemId: 'glue', quantity: 2 }],
    });
    expect(await screen.findByText('Not priced')).toBeInTheDocument();
  });

  it('does not submit the page form on Enter in an inline field', async () => {
    stubCatalog();
    const onFormSubmit = vi.fn();
    const user = renderEditor({ initial: [screwLine], onFormSubmit });
    await screen.findByText('Hardware · Production');
    const input = screen.getByLabelText('Quantity for M3 screw');
    await user.click(input);
    await user.keyboard('{Enter}');
    expect(onFormSubmit).not.toHaveBeenCalled();
    expect(input).not.toHaveFocus();
  });

  it('keeps a dialog submit from submitting the page form', async () => {
    stubCatalog();
    const onFormSubmit = vi.fn();
    const user = renderEditor({ initial: [screwLine], onFormSubmit });
    await screen.findByText('Hardware · Production');
    await user.click(screen.getByRole('button', { name: 'Edit M3 screw' }));
    const quantity = await screen.findByLabelText('Quantity (piece)');
    await user.clear(quantity);
    await user.type(quantity, '6');
    // user-event cannot press Enter for a number input whose submit button sits outside the
    // form, so dispatch the submit that a browser's implicit submission would fire.
    fireEvent.submit(document.getElementById('cost-line-form') as HTMLElement);
    await waitFor(() =>
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument(),
    );
    expect(draft()).toContain('"quantity":6');
    expect(onFormSubmit).not.toHaveBeenCalled();
  });

  it('offers a retry when the catalog fails to load', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(json({}, 500))
      .mockResolvedValue(json({ items: catalog, totalCount: catalog.length }));
    vi.stubGlobal('fetch', fetchMock);
    const user = renderEditor();
    expect(
      await screen.findByText('Could not load cost items'),
    ).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /retry/i }));
    expect(await screen.findByText('No cost items')).toBeInTheDocument();
  });
});
