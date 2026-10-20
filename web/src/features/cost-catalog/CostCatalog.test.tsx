import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { CostCatalogPage } from './CostCatalogPage';
import { CostItemFormPage } from './CostItemFormPage';

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
const bubbleWrap = {
  id: 'wrap',
  name: 'Bubble wrap',
  effectiveRate: 0.4,
  pricing: {
    itemType: 'Physical',
    category: 'Packaging',
    unit: 'SquareMetre',
    pricingMode: 'Package',
    packageLabel: 'roll',
    packagePrice: 40,
    packageQuantity: 100,
    length: 100,
    lengthUnit: 'm',
    width: 100,
    widthUnit: 'cm',
  },
};
function renderPage(element: React.ReactNode) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  const router = createMemoryRouter([
    { path: '/', element },
    { path: '/cost-catalog', element: <div>Catalog destination</div> },
    { path: '/cost-catalog/:id', element: <div>Edit destination</div> },
  ]);
  return {
    router,
    ...render(
      <QueryClientProvider client={client}>
        <RouterProvider router={router} />
      </QueryClientProvider>,
    ),
  };
}
async function choose(
  user: ReturnType<typeof userEvent.setup>,
  label: string,
  option: string,
) {
  const select = screen.queryByRole('combobox', { name: label });
  if (select) {
    await user.click(select);
    await user.click(await screen.findByRole('option', { name: option }));
  } else {
    const group = screen.getByRole('radiogroup', { name: label });
    await user.click(within(group).getByRole('radio', { name: option }));
  }
}
afterEach(() => vi.unstubAllGlobals());

describe('Cost catalog', () => {
  it('saves roll dimensions and package price with a calculated rate', async () => {
    let body: unknown;
    vi.stubGlobal(
      'fetch',
      vi.fn((_url, init) => {
        body = JSON.parse(init.body);
        return Promise.resolve(json(bubbleWrap, 201));
      }),
    );
    renderPage(<CostItemFormPage mode="add" />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Name'), 'Bubble wrap');
    await choose(user, 'Category', 'Packaging');
    await user.type(screen.getByLabelText('Price'), '40');
    await choose(user, 'Unit', 'Area (m²)');
    await user.click(
      screen.getByRole('button', { name: 'Use length × width' }),
    );
    await user.clear(screen.getByLabelText('Length'));
    await user.type(screen.getByLabelText('Length'), '100');
    await user.clear(screen.getByLabelText('Width'));
    await user.type(screen.getByLabelText('Width'), '100');
    await choose(user, 'Width unit', 'cm');
    expect(screen.getByText('0.40 / m²')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Add cost item' }));
    await screen.findByText('Edit destination');
    expect(body).toMatchObject({
      name: 'Bubble wrap',
      pricing: {
        itemType: 'Physical',
        category: 'Packaging',
        unit: 'SquareMetre',
        pricingMode: 'Package',
        packagePrice: 40,
        packageLabel: null,
        length: 100,
        lengthUnit: 'm',
        width: 100,
        widthUnit: 'cm',
      },
    });
  });

  it('supports keyboard row actions and restores focus after canceling removal', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() =>
        Promise.resolve(json({ items: [bubbleWrap], totalCount: 1 })),
      ),
    );
    renderPage(<CostCatalogPage />);
    const user = userEvent.setup();
    const actions = await screen.findByRole('button', {
      name: 'Actions for Bubble wrap',
    });
    actions.focus();
    await user.keyboard('{Enter}');
    expect(
      await screen.findByRole('menuitem', { name: 'Edit' }),
    ).toBeInTheDocument();
    await user.keyboard('{ArrowDown}{Enter}');
    expect(await screen.findByRole('alertdialog')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(actions).toHaveFocus());
    expect(
      screen.getByRole('heading', { name: 'Cost catalog' }),
    ).toBeInTheDocument();
  });
});

describe('Guided cost editor', () => {
  function stubSave(response: unknown) {
    const calls: { body?: unknown } = {};
    vi.stubGlobal(
      'fetch',
      vi.fn((_url, init) => {
        calls.body = JSON.parse(init.body);
        return Promise.resolve(json(response, 201));
      }),
    );
    return calls;
  }
  function renderEdit(
    item: { id: string; name: string },
    onPut?: (body: unknown) => void,
  ) {
    vi.stubGlobal(
      'fetch',
      vi.fn((_url, init) => {
        if (init?.method === 'PUT') onPut?.(JSON.parse(init.body));
        return Promise.resolve(json(item));
      }),
    );
    const router = createMemoryRouter(
      [
        {
          path: '/cost-catalog/:id',
          element: <CostItemFormPage mode="edit" />,
        },
      ],
      { initialEntries: [`/cost-catalog/${item.id}`] },
    );
    render(
      <QueryClientProvider
        client={
          new QueryClient({
            defaultOptions: {
              queries: { retry: false },
              mutations: { retry: false },
            },
          })
        }
      >
        <RouterProvider router={router} />
      </QueryClientProvider>,
    );
  }

  it('saves a price for one unit as a direct rate', async () => {
    const saved = stubSave({ id: 'bag', name: 'Courier bag', pricing: {} });
    renderPage(<CostItemFormPage mode="add" />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Name'), 'Courier bag');
    await choose(user, 'Category', 'Packaging');
    await user.type(screen.getByLabelText('Price'), '0.3');
    expect(screen.getByText('0.30 / piece')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Add cost item' }));
    await screen.findByText('Edit destination');
    expect(saved.body).toMatchObject({
      name: 'Courier bag',
      pricing: {
        itemType: 'Physical',
        category: 'Packaging',
        unit: 'Piece',
        pricingMode: 'Direct',
        directRate: 0.3,
        packageQuantity: null,
      },
    });
  });

  it('saves a pack price and shows the cost per unit', async () => {
    const saved = stubSave({ id: 'screw', name: 'Screw M3', pricing: {} });
    renderPage(<CostItemFormPage mode="add" />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Name'), 'Screw M3');
    await user.type(screen.getByLabelText('Price'), '8');
    await user.type(screen.getByLabelText('Quantity'), '200');
    expect(screen.getByText('0.04 / piece')).toBeInTheDocument();
    expect(screen.getByText('8.00 for 200 pieces')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Add cost item' }));
    await screen.findByText('Edit destination');
    expect(saved.body).toMatchObject({
      pricing: {
        pricingMode: 'Package',
        packagePrice: 8,
        packageQuantity: 200,
        packageLabel: null,
        directRate: null,
      },
    });
  });

  it('keeps price and quantity when the unit changes', async () => {
    vi.stubGlobal('fetch', vi.fn());
    renderPage(<CostItemFormPage mode="add" />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Price'), '40');
    await user.type(screen.getByLabelText('Quantity'), '100');
    await choose(user, 'Unit', 'Area (m²)');
    expect(screen.getByLabelText('Price')).toHaveValue(40);
    expect(screen.getByLabelText('Quantity')).toHaveValue(100);
    expect(screen.getByText('0.40 / m²')).toBeInTheDocument();
  });

  it('offers hourly pricing for labor and hides quantity', async () => {
    const saved = stubSave({
      id: 'labor',
      name: 'Assembly labor',
      pricing: {},
    });
    renderPage(<CostItemFormPage mode="add" />);
    const user = userEvent.setup();
    await choose(user, 'Category', 'Labor');
    expect(screen.getByRole('combobox', { name: 'Unit' })).toHaveTextContent(
      'Per hour',
    );
    expect(screen.queryByLabelText('Quantity')).not.toBeInTheDocument();
    await user.type(screen.getByLabelText('Name'), 'Assembly labor');
    await user.type(screen.getByLabelText('Price'), '20');
    expect(screen.getByText('20.00 / hour')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Add cost item' }));
    await screen.findByText('Edit destination');
    expect(saved.body).toMatchObject({
      name: 'Assembly labor',
      pricing: {
        itemType: 'Service',
        category: 'Labour',
        unit: 'Hour',
        pricingMode: 'Direct',
        directRate: 20,
      },
    });
  });

  it('moves an untouched unit with the category but keeps a chosen unit', async () => {
    vi.stubGlobal('fetch', vi.fn());
    renderPage(<CostItemFormPage mode="add" />);
    const user = userEvent.setup();
    const unit = () => screen.getByRole('combobox', { name: 'Unit' });
    await choose(user, 'Category', 'Shipping');
    expect(unit()).toHaveTextContent('Per product');
    await choose(user, 'Category', 'Hardware');
    expect(unit()).toHaveTextContent('piece');
    await choose(user, 'Unit', 'Area (m²)');
    await choose(user, 'Category', 'Labor');
    expect(unit()).toHaveTextContent('m²');
  });

  it('shows inline validation and keeps incomplete dimension inputs visible', async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
    renderPage(<CostItemFormPage mode="add" />);
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Add cost item' }));
    expect(await screen.findByText('Name is required.')).toBeInTheDocument();
    await waitFor(() => expect(screen.getByLabelText('Name')).toHaveFocus());
    await user.type(screen.getByLabelText('Name'), 'Wrap');
    await choose(user, 'Unit', 'Area (m²)');
    await user.click(
      screen.getByRole('button', { name: 'Use length × width' }),
    );
    await user.click(screen.getByRole('button', { name: 'Add cost item' }));
    expect(await screen.findByText('Length must be > 0.')).toBeInTheDocument();
    expect(screen.getByText('Width must be > 0.')).toBeInTheDocument();
    expect(screen.getByLabelText('Length')).toHaveAttribute(
      'aria-invalid',
      'true',
    );
    expect(screen.getByLabelText('Width')).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('rejects fractional pieces and a zero quantity', async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
    renderPage(<CostItemFormPage mode="add" />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Name'), 'Screw');
    await user.type(screen.getByLabelText('Quantity'), '2.5');
    await user.click(screen.getByRole('button', { name: 'Add cost item' }));
    expect(
      await screen.findByText('Quantity must be a positive integer.'),
    ).toBeInTheDocument();
    await choose(user, 'Unit', 'Weight (g)');
    await waitFor(() =>
      expect(
        screen.queryByText('Quantity must be a positive integer.'),
      ).not.toBeInTheDocument(),
    );
    await user.clear(screen.getByLabelText('Quantity'));
    await user.type(screen.getByLabelText('Quantity'), '0');
    expect(
      await screen.findByText('Quantity must be > 0.'),
    ).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('saves a blank price as not priced', async () => {
    const saved = stubSave({ id: 'tape', name: 'Tape', pricing: {} });
    renderPage(<CostItemFormPage mode="add" />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Name'), 'Tape');
    expect(screen.getByText('Not priced')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Add cost item' }));
    await screen.findByText('Edit destination');
    expect(saved.body).toMatchObject({
      pricing: { pricingMode: 'Direct', directRate: null },
    });
  });

  it('loads a direct row as the price of one unit', async () => {
    renderEdit({
      id: 'bag',
      name: 'Courier bag',
      effectiveRate: 0.3,
      pricing: {
        itemType: 'Physical',
        category: 'Packaging',
        unit: 'Piece',
        pricingMode: 'Direct',
        directRate: 0.3,
      },
    } as never);
    await screen.findByRole('heading', { name: 'Courier bag' });
    expect(screen.getByLabelText('Price')).toHaveValue(0.3);
    expect(screen.getByLabelText('Quantity')).toHaveValue(null);
    expect(screen.getByText('0.30 / piece')).toBeInTheDocument();
  });

  it('keeps a legacy package label when a package is saved', async () => {
    let body: unknown;
    renderEdit(
      {
        id: 'tape',
        name: 'Tape',
        effectiveRate: 0.06,
        pricing: {
          itemType: 'Physical',
          category: 'Packaging',
          unit: 'Metre',
          pricingMode: 'Package',
          packageLabel: 'roll',
          packagePrice: 3,
          packageQuantity: 50,
        },
      } as never,
      (put) => {
        body = put;
      },
    );
    const user = userEvent.setup();
    await screen.findByRole('heading', { name: 'Tape' });
    await user.clear(screen.getByLabelText('Price'));
    await user.type(screen.getByLabelText('Price'), '4');
    await user.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(body).toBeDefined());
    expect(body).toMatchObject({
      pricing: {
        pricingMode: 'Package',
        packageLabel: 'roll',
        packagePrice: 4,
        packageQuantity: 50,
      },
    });
  });

  it('edits in place and resets the draft after save', async () => {
    let puts = 0;
    vi.stubGlobal(
      'fetch',
      vi.fn((_url, init) => {
        if (init?.method === 'PUT') {
          puts++;
          return Promise.resolve(
            json({
              ...bubbleWrap,
              pricing: { ...bubbleWrap.pricing, packagePrice: 80 },
              effectiveRate: 0.8,
            }),
          );
        }
        return Promise.resolve(json(bubbleWrap));
      }),
    );
    const client = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });
    const router = createMemoryRouter(
      [
        {
          path: '/cost-catalog/:id',
          element: <CostItemFormPage mode="edit" />,
        },
      ],
      { initialEntries: ['/cost-catalog/wrap'] },
    );
    render(
      <QueryClientProvider client={client}>
        <RouterProvider router={router} />
      </QueryClientProvider>,
    );
    const user = userEvent.setup();
    await screen.findByRole('heading', { name: 'Bubble wrap' });
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled(),
    );
    await user.clear(screen.getByLabelText('Length'));
    await user.clear(screen.getByLabelText('Width'));
    expect(
      within(screen.getByText('Cost').parentElement as HTMLElement).getByText(
        'Not priced',
      ),
    ).toBeInTheDocument();
    await user.type(screen.getByLabelText('Length'), '100');
    await user.type(screen.getByLabelText('Width'), '100');
    await user.clear(screen.getByLabelText('Price'));
    await user.type(screen.getByLabelText('Price'), '80');
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled();
    await user.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled(),
    );
    expect(router.state.location.pathname).toBe('/cost-catalog/wrap');
    expect(puts).toBe(1);
    expect(screen.queryByText('Unsaved changes')).not.toBeInTheDocument();
  });

  it('saves the picked icon', async () => {
    const saved = stubSave({ id: 'magnet', name: 'Magnet', pricing: {} });
    renderPage(<CostItemFormPage mode="add" />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Name'), 'Magnet');
    await user.click(screen.getByRole('button', { name: 'Icon' }));
    await user.type(screen.getByPlaceholderText('Search icons'), 'magnet');
    await user.click(await screen.findByRole('option', { name: 'Magnet' }));
    await user.click(screen.getByRole('button', { name: 'Add cost item' }));
    await screen.findByText('Edit destination');
    expect(saved.body).toMatchObject({ name: 'Magnet', icon: 'magnet' });
  });

  it('sends a null icon when Default is chosen', async () => {
    let put: unknown;
    const gift = { ...bubbleWrap, icon: 'gift' };
    renderEdit(gift, (body) => {
      put = body;
    });
    const user = userEvent.setup();
    await screen.findByRole('heading', { name: 'Bubble wrap' });
    await user.click(screen.getByRole('button', { name: 'Icon' }));
    await user.click(await screen.findByRole('option', { name: 'Default' }));
    await user.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(put).toMatchObject({ icon: null }));
  });

  it('finds icons by either spelling of disc and by roll', async () => {
    stubSave({ id: 'x', name: 'x', pricing: {} });
    renderPage(<CostItemFormPage mode="add" />);
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Icon' }));
    const search = screen.getByPlaceholderText('Search icons');
    await user.type(search, 'disk');
    expect(await screen.findByRole('option', { name: 'Disc' })).toBeVisible();
    expect(screen.getByRole('option', { name: 'Magnet' })).toBeVisible();
    await user.clear(search);
    await user.type(search, 'roll');
    expect(await screen.findByRole('option', { name: 'Roll' })).toBeVisible();
    expect(screen.getByRole('option', { name: 'Bubble wrap' })).toBeVisible();
  });

  it('finds people icons by human or people', async () => {
    stubSave({ id: 'x', name: 'x', pricing: {} });
    renderPage(<CostItemFormPage mode="add" />);
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Icon' }));
    const search = screen.getByPlaceholderText('Search icons');
    await user.type(search, 'human');
    expect(await screen.findByRole('option', { name: 'Person' })).toBeVisible();
    expect(screen.getByRole('option', { name: 'Operator' })).toBeVisible();
    await user.clear(search);
    await user.type(search, 'people');
    expect(await screen.findByRole('option', { name: 'Team' })).toBeVisible();
    expect(screen.getByRole('option', { name: 'Avatar' })).toBeVisible();
  });
});

describe('Cost catalog table', () => {
  it('shows the chosen icon, or the category default', async () => {
    const hardware = { ...bubbleWrap.pricing, category: 'Hardware' };
    const magnet = {
      ...bubbleWrap,
      id: 'magnet',
      name: 'Magnet 6x2',
      icon: 'magnet',
      pricing: hardware,
    };
    const screw = {
      ...bubbleWrap,
      id: 'screw',
      name: 'Screw M3',
      pricing: hardware,
    };
    vi.stubGlobal(
      'fetch',
      vi.fn(() =>
        Promise.resolve(json({ items: [magnet, screw], totalCount: 2 })),
      ),
    );
    renderPage(<CostCatalogPage />);
    const row = async (name: string) =>
      (await screen.findByRole('link', { name })).closest('tr') as HTMLElement;
    expect(
      (await row('Magnet 6x2')).querySelector('.lucide-magnet'),
    ).toBeInTheDocument();
    expect(
      (await row('Screw M3')).querySelector('.lucide-screw'),
    ).toBeInTheDocument();
  });

  it('uses page-local selection, a column menu, and clears selection when filters change', async () => {
    const urls: string[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn((url: string) => {
        urls.push(url);
        return Promise.resolve(json({ items: [bubbleWrap], totalCount: 1 }));
      }),
    );
    renderPage(<CostCatalogPage />);
    const user = userEvent.setup();
    const select = await screen.findByRole('checkbox', {
      name: 'Select Bubble wrap',
    });
    select.focus();
    await user.keyboard(' ');
    expect(
      await screen.findByRole('region', { name: 'Cost items actions' }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('checkbox', {
        name: 'Select all cost items on this page',
      }),
    ).toHaveAttribute('data-state', 'checked');
    expect(screen.getByRole('button', { name: 'Columns' })).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: 'Cost catalog' }),
    ).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Clear selection' }));
    expect(
      screen.getByRole('checkbox', {
        name: 'Select all cost items on this page',
      }),
    ).toHaveFocus();
    await user.click(select);
    await user.click(screen.getByRole('radio', { name: 'Physical' }));
    await waitFor(() =>
      expect(
        screen.queryByRole('region', { name: 'Cost items actions' }),
      ).not.toBeInTheDocument(),
    );
    expect(
      urls.some(
        (url) => url.includes('itemType=Physical') && url.includes('page=1'),
      ),
    ).toBe(true);
  });

  it('removes eligible selections once, retains blocked items, and permits retry', async () => {
    const screw = {
      id: 'screw',
      name: 'Screw',
      effectiveRate: 0.05,
      pricing: { itemType: 'Physical', category: 'Hardware', unit: 'Piece' },
    };
    let items = [bubbleWrap, screw];
    const deletes: string[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn((url: string, init?: RequestInit) => {
        if (init?.method === 'DELETE') {
          deletes.push(url);
          if (url.endsWith('/screw'))
            return Promise.resolve(
              json({ detail: 'Used by an assembly.' }, 409),
            );
          items = items.filter((item) => item.id !== 'wrap');
          return Promise.resolve(new Response(null, { status: 204 }));
        }
        return Promise.resolve(json({ items, totalCount: items.length }));
      }),
    );
    renderPage(<CostCatalogPage />);
    const user = userEvent.setup();
    await user.click(
      await screen.findByRole('checkbox', {
        name: 'Select all cost items on this page',
      }),
    );
    await user.click(screen.getByRole('button', { name: 'Remove selected' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Remove' }));
    await waitFor(() =>
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument(),
    );
    expect(
      screen.getByRole('checkbox', { name: 'Select Screw' }),
    ).toHaveAttribute('data-state', 'checked');
    expect(deletes.filter((url) => url.endsWith('/wrap'))).toHaveLength(1);
    expect(deletes.filter((url) => url.endsWith('/screw'))).toHaveLength(1);
    await user.click(screen.getByRole('button', { name: 'Remove selected' }));
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', {
        name: 'Remove',
      }),
    );
    await waitFor(() =>
      expect(deletes.filter((url) => url.endsWith('/screw'))).toHaveLength(2),
    );
    expect(deletes.filter((url) => url.endsWith('/wrap'))).toHaveLength(1);
  });
});

it('disables conflicting table controls while removal is pending', async () => {
  let finish: (response: Response) => void = () => {};
  const deferred = new Promise<Response>((resolve) => {
    finish = resolve;
  });
  let deletes = 0;
  vi.stubGlobal(
    'fetch',
    vi.fn((_url: string, init?: RequestInit) => {
      if (init?.method === 'DELETE') {
        deletes++;
        return deferred;
      }
      return Promise.resolve(json({ items: [bubbleWrap], totalCount: 1 }));
    }),
  );
  renderPage(<CostCatalogPage />);
  const user = userEvent.setup();
  await user.click(
    await screen.findByRole('checkbox', { name: 'Select Bubble wrap' }),
  );
  await user.click(screen.getByRole('button', { name: 'Remove selected' }));
  const dialog = await screen.findByRole('alertdialog');
  await user.click(within(dialog).getByRole('button', { name: 'Remove' }));
  expect(
    within(dialog).getByRole('button', { name: /Removing/ }),
  ).toBeDisabled();
  expect(
    screen.getByRole('checkbox', { name: 'Select Bubble wrap', hidden: true }),
  ).toBeDisabled();
  expect(
    screen.getByRole('radio', { name: 'Physical', hidden: true }),
  ).toBeDisabled();
  await act(async () => {
    finish(json({ detail: 'Used by an assembly.' }, 409));
  });
  await waitFor(() =>
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument(),
  );
  expect(deletes).toBe(1);
  expect(
    screen.getByRole('checkbox', { name: 'Select Bubble wrap' }),
  ).toBeEnabled();
});
