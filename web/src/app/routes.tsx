import { createBrowserRouter } from 'react-router';
import { ActivityLogPage } from '../features/activity-log/ActivityLogPage';
import { LoginPage } from '../features/auth/LoginPage';
import { ConnectionSecretsPage } from '../features/connection-secrets/ConnectionSecretsPage';
import { DashboardPage } from '../features/dashboard/DashboardPage';
import { AppShell } from './layout/AppShell';
import { ProtectedRoute } from './layout/ProtectedRoute';

export const router = createBrowserRouter([
  {
    path: '/login',
    element: <LoginPage />,
  },
  {
    element: <ProtectedRoute />,
    children: [
      {
        element: <AppShell />,
        children: [
          { path: '/', element: <DashboardPage /> },
          { path: '/connection-secrets', element: <ConnectionSecretsPage /> },
          { path: '/activity-log', element: <ActivityLogPage /> },
        ],
      },
    ],
  },
]);
