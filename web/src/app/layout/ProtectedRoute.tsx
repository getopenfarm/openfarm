import { Navigate, Outlet, useLocation } from 'react-router';
import { ErrorState } from '@/components/empty-state/EmptyState';
import { Skeleton } from '@/components/ui/skeleton';
import { AccessContext } from '@/lib/auth/AccessContext';
import {
  routePermission,
  settingsPath,
  useIdentity,
} from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';

export function ProtectedRoute() {
  const auth = useAuth();
  const location = useLocation();
  const identity = useIdentity();
  if (auth.isLoading) return <Skeleton className="m-4 h-40" />;
  if (!auth.isAuthenticated) {
    if (auth.error)
      return (
        <ErrorState
          title="Could not restore your session"
          onRetry={auth.retry}
        />
      );
    return (
      <Navigate
        to={`/login?returnTo=${encodeURIComponent(location.pathname + location.search)}`}
        replace
      />
    );
  }
  // A failed background poll keeps the cached identity.
  if (identity.isError && !identity.data)
    return (
      <ErrorState
        title="Could not load your account"
        onRetry={() => void identity.refetch()}
      />
    );
  if (!identity.data) return <Skeleton className="m-4 h-40" />;
  const permissions = new Set(identity.data.permissions);
  const can = (key: string | null) => key === null || permissions.has(key);
  if (!can(routePermission(location.pathname))) {
    const fallback =
      (can(routePermission('/')) ? '/' : settingsPath(can)) ?? '/profile';
    return <Navigate to={fallback} replace />;
  }
  return (
    <AccessContext.Provider value={permissions}>
      <Outlet />
    </AccessContext.Provider>
  );
}
