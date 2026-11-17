import { useEffect, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { ErrorState } from '@/components/empty-state/EmptyState';
import { Button } from '@/components/ui/button';
import { Spinner } from '@/components/ui/spinner';
import { localReturnTo } from '@/lib/auth/oidc';
import { refreshSession } from '@/lib/auth/session';
import { useAuth } from '@/lib/auth/useAuth';
import { ConnectFrame } from './ConnectErrorPage';

export function OidcCompletePage() {
  const auth = useAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [failed, setFailed] = useState(false);
  const target = localReturnTo(params.get('returnTo'));
  const login = auth.login;
  useEffect(() => {
    let active = true;
    void refreshSession()
      .then((token) => {
        if (!active) return;
        if (!token) {
          setFailed(true);
          return;
        }
        login(token);
        navigate(target, { replace: true });
      })
      .catch(() => {
        if (active) setFailed(true);
      });
    return () => {
      active = false;
    };
  }, [login, navigate, target]);
  return (
    <ConnectFrame>
      {failed ? (
        <>
          <ErrorState
            title="Could not finish sign-in"
            onRetry={() => window.location.reload()}
            description="Check that cookies are allowed, then try signing in again."
          />
          <Button asChild>
            <Link to="/login">Return to sign in</Link>
          </Button>
        </>
      ) : (
        <>
          <Spinner />
          <p>Finishing sign-in…</p>
        </>
      )}
    </ConnectFrame>
  );
}
