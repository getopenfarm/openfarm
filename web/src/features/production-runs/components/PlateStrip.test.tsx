import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { daysUntil, describeDue } from '../lib/dueDate';
import { PlateStrip } from './PlateStrip';

describe('PlateStrip', () => {
  it('draws one cell per plate and says what each state holds', () => {
    render(
      <PlateStrip
        plates={{ completed: 6, printing: 2, needsAction: 1, queued: 1 }}
      />,
    );

    const strip = screen.getByRole('img', {
      name: '6 of 10 plates done, 2 printing, 1 failed, 1 waiting',
    });
    expect(strip.children).toHaveLength(10);
  });

  it('switches to proportional segments past 60 plates', () => {
    render(<PlateStrip plates={{ completed: 150, queued: 50 }} />);

    const strip = screen.getByRole('img', {
      name: '150 of 200 plates done, 50 waiting',
    });
    expect(strip.children).toHaveLength(2);
    expect((strip.children[0] as HTMLElement).style.width).toBe('75%');
  });

  it('draws nothing without plates', () => {
    const { container } = render(<PlateStrip plates={{}} />);
    expect(container).toBeEmptyDOMElement();
  });
});

describe('describeDue', () => {
  const today = new Date(2026, 9, 28);

  it('counts calendar days', () => {
    expect(daysUntil('2026-10-31', today)).toBe(3);
    expect(daysUntil('2026-10-28', today)).toBe(0);
    expect(daysUntil('2026-10-26', today)).toBe(-2);
  });

  it('flags overdue and soon', () => {
    expect(describeDue('2026-10-26', today)).toMatchObject({
      relative: '2 d overdue',
      overdue: true,
      soon: false,
    });
    expect(describeDue('2026-10-29', today)).toMatchObject({
      relative: 'Tomorrow',
      soon: true,
    });
    expect(describeDue('2026-11-10', today)).toMatchObject({
      relative: 'In 13 d',
      overdue: false,
      soon: false,
    });
  });
});
