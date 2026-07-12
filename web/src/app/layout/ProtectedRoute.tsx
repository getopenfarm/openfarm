import { Navigate, Outlet } from 'react-router';
import { useAuth } from '@/lib/auth/useAuth';

export function ProtectedRoute() {
  const { isAuthenticated } = useAuth();

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  return <Outlet />;
}
