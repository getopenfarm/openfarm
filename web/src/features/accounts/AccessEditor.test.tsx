import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { PermissionButton } from '@/components/PermissionButton';
import { AccessContext } from '@/lib/auth/AccessContext';
import { AccessEditor } from './AccessEditor';

const leaf = (
  key: string,
  label: string,
  tier: 'read' | 'write' | 'full',
  extra: { verb?: string; phrase?: string; explicit?: boolean } = {},
) => ({
  key,
  area: key.split(':')[0],
  label,
  description: `${label} things.`,
  isRead: tier === 'read',
  isSensitive: !!extra.explicit,
  isDestructive: tier === 'full',
  isExplicit: !!extra.explicit,
  verb: extra.verb ?? null,
  phrase: extra.phrase ?? null,
});
const areas = [
  { key: 'parts', label: 'Parts', noun: 'parts', administratorOnly: false },
  { key: 'tags', label: 'Tags', noun: 'tags', administratorOnly: false },
  {
    key: 'users',
    label: 'Users & Groups',
    noun: 'users',
    administratorOnly: true,
  },
];
const permissions = [
  leaf('parts:view', 'View', 'read', { verb: 'see' }),
  leaf('parts:edit', 'Change', 'write', { verb: 'change' }),
  leaf('parts:delete', 'Delete', 'full', { verb: 'delete' }),
  leaf('parts:control', 'Control', 'write', {
    phrase: 'control parts',
    explicit: true,
  }),
  leaf('tags:list', 'View', 'read', { verb: 'see' }),
  leaf('users:manage', 'Manage', 'write', { phrase: 'manage users' }),
];

function Editor({ inherited = [] }: { inherited?: string[] }) {
  const [value, setValue] = useState<string[]>([]);
  return (
    <>
      <AccessEditor
        areas={areas}
        permissions={permissions}
        value={value}
        onChange={setValue}
        inherited={inherited}
      />
      <output aria-label="Chosen">{value.join(',')}</output>
    </>
  );
}
const chosen = () => screen.getByLabelText('Chosen');

describe('AccessEditor', () => {
  it('picks a level from the radio group and describes it', async () => {
    const user = userEvent.setup();
    render(<Editor />);
    const parts = screen.getByRole('radiogroup', { name: 'Parts access' });
    expect(within(parts).getByRole('radio', { name: 'None' })).toBeChecked();
    expect(screen.queryByText('Users & Groups')).not.toBeInTheDocument();
    // Tags has one levelled leaf: None/View only, no Custom.
    expect(
      screen.getByRole('radiogroup', { name: 'Tags access' }),
    ).not.toHaveTextContent('Custom');

    await user.click(screen.getAllByRole('radio', { name: 'Edit' })[0]);
    expect(chosen()).toHaveTextContent(/^parts:view,parts:edit$/);
    expect(parts).toHaveTextContent('Edit');
    expect(
      screen.getByText('Can see and change parts. Cannot delete.'),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('switch', { name: 'Parts: Control' }),
    ).not.toBeChecked();
  });

  it('opens Custom for per-ability switches and keeps it open', async () => {
    const user = userEvent.setup();
    render(<Editor />);
    expect(
      screen.queryByRole('switch', { name: 'Parts: Delete' }),
    ).not.toBeInTheDocument();
    await user.click(screen.getByRole('radio', { name: 'Custom' }));
    await user.click(screen.getByRole('switch', { name: 'Parts: View' }));
    // Lands on View, but the panel stays open under the pointer.
    expect(chosen()).toHaveTextContent(/^parts:view$/);
    expect(screen.getByRole('radio', { name: 'Custom' })).toBeChecked();
    await user.click(screen.getByRole('switch', { name: 'Parts: Delete' }));
    expect(chosen()).toHaveTextContent(/^parts:view,parts:delete$/);
  });

  it('keeps explicit abilities out of every level', async () => {
    const user = userEvent.setup();
    render(<Editor />);
    await user.click(screen.getByRole('switch', { name: 'Parts: Control' }));
    await user.click(screen.getAllByRole('radio', { name: 'Full' })[0]);
    expect(chosen()).toHaveTextContent(
      /^parts:view,parts:edit,parts:delete,parts:control$/,
    );
    await user.click(screen.getAllByRole('radio', { name: 'None' })[0]);
    expect(chosen()).toHaveTextContent(/^parts:control$/);
  });

  it('locks inherited abilities and blocks lower levels', async () => {
    const user = userEvent.setup();
    render(<Editor inherited={['parts:edit']} />);
    const none = screen.getAllByRole('radio', { name: 'None' })[0];
    expect(none).toBeDisabled();
    expect(none).toHaveAttribute(
      'title',
      'A group already grants more than this.',
    );
    await user.click(screen.getByRole('radio', { name: 'Custom' }));
    const inherited = screen.getByRole('switch', { name: 'Parts: Change' });
    expect(inherited).toBeChecked();
    expect(inherited).toBeDisabled();
    expect(screen.getByText('From a group')).toBeInTheDocument();
    await user.click(screen.getAllByRole('radio', { name: 'Full' })[0]);
    expect(chosen()).toHaveTextContent(/^parts:view,parts:delete$/);
  });

  it('hides mutation controls from users without the ability', () => {
    render(
      <AccessContext.Provider value={new Set(['parts:view'])}>
        <PermissionButton permission="parts:edit">Save</PermissionButton>
        <PermissionButton>Back</PermissionButton>
      </AccessContext.Provider>,
    );
    expect(
      screen.queryByRole('button', { name: 'Save' }),
    ).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Back' })).toBeInTheDocument();
  });
});
