import { createBrowserRouter } from 'react-router';
import { ActivityLogPage } from '../features/activity-log/ActivityLogPage';
import { AssembliesPage } from '../features/assemblies/AssembliesPage';
import { AssemblyFormPage } from '../features/assemblies/AssemblyFormPage';
import { LoginPage } from '../features/auth/LoginPage';
import { ConnectionSecretsPage } from '../features/connection-secrets/ConnectionSecretsPage';
import { DashboardPage } from '../features/dashboard/DashboardPage';
import { FilamentFormPage } from '../features/filament/FilamentFormPage';
import { FilamentPage } from '../features/filament/FilamentPage';
import { JobsPage } from '../features/jobs/JobsPage';
import { MaintenancePage } from '../features/maintenance/MaintenancePage';
import { PartAddPage } from '../features/parts/PartAddPage';
import { PartEditPage } from '../features/parts/PartEditPage';
import { PartsPage } from '../features/parts/PartsPage';
import { PrinterDetailPage } from '../features/printers/PrinterDetailPage';
import { PrintersPage } from '../features/printers/PrintersPage';
import { ProfilesPage } from '../features/profiles/ProfilesPage';
import { SettingsPage } from '../features/settings/SettingsPage';
import { SpoolsPage } from '../features/spools/SpoolsPage';
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
            handle: { title: 'Printers' },
            children: [
              { index: true, element: <PrintersPage /> },
              {
                path: ':id',
                element: <PrinterDetailPage />,
                handle: { title: 'Printer Detail' },
              },
            ],
          },
          { path: '/jobs', element: <JobsPage />, handle: { title: 'Jobs' } },
          {
            path: '/filament',
            handle: { title: 'Filament' },
            children: [
              { index: true, element: <FilamentPage /> },
              {
                path: 'new',
                element: <FilamentFormPage mode="add" />,
                handle: { title: 'Add Filament' },
              },
              {
                path: ':id/edit',
                element: <FilamentFormPage mode="edit" />,
                handle: { title: 'Edit Filament' },
              },
            ],
          },
          {
            path: '/spools',
            element: <SpoolsPage />,
            handle: { title: 'Spools' },
          },
          {
            path: '/parts',
            handle: { title: 'Parts' },
            children: [
              { index: true, element: <PartsPage /> },
              {
                path: 'new',
                element: <PartAddPage />,
                handle: { title: 'Add Part' },
              },
              {
                path: ':id/edit',
                element: <PartEditPage />,
                handle: { title: 'Edit Part' },
              },
            ],
          },
          {
            path: '/assemblies',
            handle: { title: 'Assemblies' },
            children: [
              { index: true, element: <AssembliesPage /> },
              {
                path: 'new',
                element: <AssemblyFormPage mode="add" />,
                handle: { title: 'New Assembly' },
              },
              {
                path: ':id/edit',
                element: <AssemblyFormPage mode="edit" />,
                handle: { title: 'Edit Assembly' },
              },
            ],
          },
          {
            path: '/profiles',
            element: <ProfilesPage />,
            handle: { title: 'Profiles' },
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
