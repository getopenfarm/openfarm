import { createBrowserRouter } from 'react-router';
import { ActivityLogPage } from '../features/activity-log/ActivityLogPage';
import { AssembliesPage } from '../features/assemblies/AssembliesPage';
import { LoginPage } from '../features/auth/LoginPage';
import { ConnectionSecretsPage } from '../features/connection-secrets/ConnectionSecretsPage';
import { DashboardPage } from '../features/dashboard/DashboardPage';
import { FilamentPage } from '../features/filament/FilamentPage';
import { JobsPage } from '../features/jobs/JobsPage';
import { MaintenancePage } from '../features/maintenance/MaintenancePage';
import { PartsPage } from '../features/parts/PartsPage';
import { PrintersPage } from '../features/printers/PrintersPage';
import { SettingsPage } from '../features/settings/SettingsPage';
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
          {
            path: '/',
            element: <DashboardPage />,
            handle: { title: 'Dashboard' },
          },
          {
            path: '/printers',
            element: <PrintersPage />,
            handle: { title: 'Printers' },
          },
          { path: '/jobs', element: <JobsPage />, handle: { title: 'Jobs' } },
          {
            path: '/filament',
            element: <FilamentPage />,
            handle: { title: 'Filament' },
          },
          {
            path: '/parts',
            element: <PartsPage />,
            handle: { title: 'Parts' },
          },
          {
            path: '/assemblies',
            element: <AssembliesPage />,
            handle: { title: 'Assemblies' },
          },
          {
            path: '/maintenance',
            element: <MaintenancePage />,
            handle: { title: 'Maintenance' },
          },
          {
            path: '/activity-log',
            element: <ActivityLogPage />,
            handle: { title: 'Activity Log' },
          },
          {
            path: '/settings',
            handle: { title: 'Settings' },
            children: [
              { index: true, element: <SettingsPage /> },
              {
                path: 'connection-secrets',
                element: <ConnectionSecretsPage />,
                handle: { title: 'Connection Secrets' },
              },
            ],
          },
        ],
      },
    ],
  },
]);
