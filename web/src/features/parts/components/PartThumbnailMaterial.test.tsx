import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { PartThumbnailMaterial } from './PartThumbnailMaterial';

describe('thumbnail material overlay', () => {
  it('bounds multiple materials and keeps the full accessible label without a tooltip', async () => {
    const user = userEvent.setup();
    render(
      <PartThumbnailMaterial
        part={{ materialTypes: ['PLA', 'PETG', 'ABS', 'PLA'] }}
        disabled={false}
      />,
    );
    expect(screen.getByText('PLA')).toBeInTheDocument();
    expect(screen.getByText('+2')).toBeInTheDocument();
    const badge = screen.getByRole('button', {
      name: 'Material: PLA, PETG, ABS',
    });
    await user.tab();
    expect(badge).toHaveFocus();
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
    await user.hover(badge);
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
    await user.keyboard('{Enter}');
    expect(badge).toHaveFocus();
  });

  it.each([true, false])(
    'preserves unknown/any material semantics (requiresSlicing=%s)',
    (requiresSlicing) => {
      render(
        <PartThumbnailMaterial part={{ requiresSlicing }} disabled={false} />,
      );
      expect(
        screen.getByRole('button', {
          name: requiresSlicing
            ? 'Material: Any material'
            : 'Material: Unknown material',
        }),
      ).toBeInTheDocument();
    },
  );

  it('disables the trigger during pending operations', () => {
    render(
      <PartThumbnailMaterial part={{ materialTypes: ['PLA'] }} disabled />,
    );
    expect(
      screen.getByRole('button', { name: 'Material: PLA' }),
    ).toBeDisabled();
  });
});
