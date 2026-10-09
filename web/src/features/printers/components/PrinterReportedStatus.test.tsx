import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ActivityDetails } from '@/features/activity-log/components/ActivityDetails';
import { PrinterReportedStatus } from './PrinterReportedStatus';

const warning = {
  source: 'Hms',
  code: '0300_0100_0002_0001',
  severity: 'Warning',
  message: 'Filament needs attention.',
  troubleshootingUrl: 'https://wiki.bambulab.com/en/hms/home',
};

describe('reported printer state and alerts', () => {
  it('shows exact state and active warning with code and troubleshooting link', () => {
    render(
      <PrinterReportedStatus
        isReachable
        telemetry={{ reportedState: 'PAUSE', activeAlerts: [warning] }}
      />,
    );
    expect(screen.getByText(/Paused \(PAUSE\)/)).toBeInTheDocument();
    expect(screen.getByLabelText('Active printer alerts')).toHaveTextContent(
      warning.message,
    );
    expect(screen.getByText(warning.code)).toBeInTheDocument();
    expect(
      screen.getByRole('link', { name: 'Troubleshooting' }),
    ).toHaveAttribute('href', warning.troubleshootingUrl);
  });
  it('labels offline observations and unknown state and severity', () => {
    render(
      <PrinterReportedStatus
        isReachable={false}
        telemetry={{
          reportedState: 'NEW_STATE',
          activeAlerts: [{ ...warning, severity: 'Unknown' }],
        }}
      />,
    );
    expect(
      screen.getByText(/Last-known reported state: NEW_STATE/),
    ).toBeInTheDocument();
    expect(
      screen.getByLabelText('Last-known printer alerts'),
    ).toHaveTextContent('Unknown');
  });
  it('renders exact history and clearing while tolerating legacy entries', () => {
    const { rerender } = render(
      <ActivityDetails
        details={{
          previousState: 'RUNNING',
          reportedState: 'PAUSE',
          previousAlerts: [warning],
          activeAlerts: [],
        }}
      />,
    );
    expect(
      screen.getByText('Printing (RUNNING) → Paused (PAUSE)'),
    ).toBeInTheDocument();
    expect(screen.getByText(/Cleared:/)).toHaveTextContent(warning.code);
    rerender(<ActivityDetails />);
    expect(screen.queryByText(/Cleared:/)).not.toBeInTheDocument();
  });
});
