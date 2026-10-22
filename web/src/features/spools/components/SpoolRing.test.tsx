import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { SpoolRing } from './SpoolRing';

const arc = (container: HTMLElement) =>
  container.querySelector('[data-slot="spool-ring-fill"]');

/** What the arc covers of a full turn, 0 to 1. */
function share(container: HTMLElement) {
  const [filled, whole] = (
    arc(container)?.getAttribute('stroke-dasharray') ?? ''
  )
    .split(' ')
    .map(Number);
  return filled / whole;
}

describe('SpoolRing', () => {
  it('draws the filament color over what is left of the spool', () => {
    const { container } = render(
      <SpoolRing colorHex="#18181b" fraction={0.4} />,
    );

    expect(arc(container)).toHaveAttribute('stroke', '#18181b');
    expect(share(container)).toBeCloseTo(0.4);
  });

  it('is a full ring by default', () => {
    const { container } = render(<SpoolRing colorHex="#18181b" />);

    expect(share(container)).toBeCloseTo(1);
  });

  it('is all hatch until there is a color', () => {
    const { container } = render(<SpoolRing fraction={0.4} />);

    expect(arc(container)).toBeNull();
  });

  it.each([
    [-0.5, 0],
    [3, 1],
  ])('keeps a fraction of %s within the ring', (fraction, expected) => {
    const { container } = render(
      <SpoolRing colorHex="#18181b" fraction={fraction} />,
    );

    expect(share(container)).toBeCloseTo(expected);
  });

  it('is hidden from assistive technology', () => {
    const { container } = render(<SpoolRing colorHex="#18181b" />);

    expect(container.querySelector('svg')).toHaveAttribute(
      'aria-hidden',
      'true',
    );
  });
});
