import { type ReactNode, useEffect } from 'react';
import { useSearchParams } from 'react-router';
import { EmptyState } from '@/components/empty-state/EmptyState';

/** Codes the server may send; anything else is never rendered (it is query text). */
const EXPIRED_CODES = new Set([
  'invalid_request',
  'invalid_client',
  'unsupported_response_type',
  'invalid_target',
  'invalid_scope',
]);

export function useConnectTitle() {
  useEffect(() => {
    const previous = document.title;
    document.title = 'Connect app - OpenFarm';
    return () => {
      document.title = previous;
    };
  }, []);
}

/** The chromeless frame of the connect pages (akaun's consent layout). */
export function ConnectFrame({ children }: { children: ReactNode }) {
  return (
    <main className="mx-auto flex min-h-svh max-w-xl flex-col justify-center gap-6 p-6">
      <img
        src={`${import.meta.env.BASE_URL}favicon.svg`}
        alt="OpenFarm"
        className="size-[54px] drop-shadow-[0_4px_10px_rgb(21_96_245/0.35)]"
      />
      {children}
    </main>
  );
}

/** Why a connection could not start, by OAuth error code. */
export function ConnectMessage({ code }: { code: string | null }) {
  if (code === 'disabled')
    return (
      <EmptyState
        title="Sign-in is off"
        description={
          <>
            Use an access token, or ask an administrator to enable sign-in in
            Settings → AI assistants.{' '}
            <a
              href="https://getopenfarm.github.io/docs/administration/ai-assistants#prepare-the-server"
              target="_blank"
              rel="noreferrer"
            >
              How to turn on sign-in
            </a>
          </>
        }
      />
    );
  if (code === 'temporarily_unavailable')
    return (
      <EmptyState
        title="Too many connection requests"
        description="Try again in a minute."
      />
    );
  return (
    <EmptyState
      title="Connection request expired"
      description={
        <>
          Start again from your AI assistant.
          {code && EXPIRED_CODES.has(code) && (
            <span className="mt-2 block font-mono text-xs">Error: {code}</span>
          )}
        </>
      }
    />
  );
}

export function ConnectErrorPage() {
  const [params] = useSearchParams();
  useConnectTitle();
  return (
    <ConnectFrame>
      <ConnectMessage code={params.get('code')} />
    </ConnectFrame>
  );
}
