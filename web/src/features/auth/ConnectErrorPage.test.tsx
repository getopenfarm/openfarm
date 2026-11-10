import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { expect, it } from 'vitest';
import { ConnectErrorPage } from './ConnectErrorPage';

const at = (code: string) =>
  render(
    <MemoryRouter initialEntries={[`/connect/error?code=${code}`]}>
      <ConnectErrorPage />
    </MemoryRouter>,
  );

it('explains that sign-in is off, with the docs link', () => {
  at('disabled');
  expect(screen.getByText('Sign-in is off')).toBeInTheDocument();
  expect(
    screen.getByRole('link', { name: 'How to turn on sign-in' }),
  ).toHaveAttribute(
    'href',
    'https://getopenfarm.github.io/docs/administration/ai-assistants#prepare-the-server',
  );
  expect(document.title).toBe('Connect app - OpenFarm');
});

it('asks to wait when the server is busy', () => {
  at('temporarily_unavailable');
  expect(screen.getByText('Too many connection requests')).toBeInTheDocument();
  expect(screen.getByText('Try again in a minute.')).toBeInTheDocument();
});

it('names a known request error code', () => {
  at('invalid_client');
  expect(screen.getByText('Connection request expired')).toBeInTheDocument();
  expect(screen.getByText('Error: invalid_client')).toBeInTheDocument();
});

it('never renders unknown query text', () => {
  const { container } = at(encodeURIComponent('<b>pwned</b> call 555'));
  expect(screen.getByText('Connection request expired')).toBeInTheDocument();
  expect(container).not.toHaveTextContent('pwned');
  expect(container).not.toHaveTextContent('Error:');
});
