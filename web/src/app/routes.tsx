import { createBrowserRouter } from 'react-router';
import { ActivityLogPage } from '../features/activity-log/ActivityLogPage';
import { AssembliesPage } from '../features/assemblies/AssembliesPage';
import { AssemblyDetailPage } from '../features/assemblies/AssemblyDetailPage';
import { AssemblyFormPage } from '../features/assemblies/AssemblyFormPage';
import { LoginPage } from '../features/auth/LoginPage';
import { ConnectionSecretFormPage } from '../features/connection-secrets/ConnectionSecretFormPage';
import { ConnectionSecretsPage } from '../features/connection-secrets/ConnectionSecretsPage';
import { DashboardPage } from '../features/dashboard/DashboardPage';
import { FilamentFormPage } from '../features/filament/FilamentFormPage';
import { FilamentPage } from '../features/filament/FilamentPage';
import { MaintenancePage } from '../features/maintenance/MaintenancePage';
import { NonPrintedComponentFormPage } from '../features/non-printed-components/NonPrintedComponentFormPage';
import { NonPrintedComponentsPage } from '../features/non-printed-components/NonPrintedComponentsPage';
import { PartDetailPage } from '../features/parts/PartDetailPage';
import { PartFormPage } from '../features/parts/PartFormPage';
import { PartsPage } from '../features/parts/PartsPage';
import { PrinterDetailPage } from '../features/printers/PrinterDetailPage';
import { PrinterFormPage } from '../features/printers/PrinterFormPage';
import { PrintersPage } from '../features/printers/PrintersPage';
import { NewProductionRunPage } from '../features/production-runs/NewProductionRunPage';
import { ProductionRunDetailPage } from '../features/production-runs/ProductionRunDetailPage';
import { ProductionRunsPage } from '../features/production-runs/ProductionRunsPage';
import { QueuePage } from '../features/production-runs/QueuePage';
import { ProfilesImportPage } from '../features/profiles/ProfilesImportPage';
import { ProfilesPage } from '../features/profiles/ProfilesPage';
import { SettingsPage } from '../features/settings/SettingsPage';
import { SlicingDefaultsPage } from '../features/settings/SlicingDefaultsPage';
import { SpoolFormPage } from '../features/spools/SpoolFormPage';
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
                path: 'new',
                element: <PrinterFormPage mode="add" />,
                handle: { title: 'Add Printer' },
              },
              {
                path: ':id',
                element: <PrinterDetailPage />,
                handle: { title: 'Printer Detail' },
              },
              {
                path: ':id/edit',
                element: <PrinterFormPage mode="edit" />,
                handle: { title: 'Edit Printer' },
              },
            ],
          },
          { path: '/jobs', element: <QueuePage />, handle: { title: 'Jobs' } },
          {
            path: '/production-runs',
            handle: { title: 'Production Runs' },
            children: [
              { index: true, element: <ProductionRunsPage /> },
              {
                path: 'new',
                element: <NewProductionRunPage />,
                handle: { title: 'New Production Run' },
              },
              {
                path: ':id',
                element: <ProductionRunDetailPage />,
                handle: { title: 'Production Run' },
              },
            ],
          },
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
            handle: { title: 'Spools' },
            children: [
              { index: true, element: <SpoolsPage /> },
              {
                path: 'new',
                element: <SpoolFormPage />,
                handle: { title: 'Add Spool' },
              },
            ],
          },
          {
            path: '/parts',
            handle: { title: 'Parts' },
            children: [
              { index: true, element: <PartsPage /> },
              {
                path: 'new',
                element: <PartFormPage />,
                handle: { title: 'Add Part' },
              },
              {
                path: ':id',
                element: <PartDetailPage />,
                handle: { title: 'Part' },
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
                path: ':id',
                element: <AssemblyDetailPage />,
                handle: { title: 'Assembly' },
              },
              {
                path: ':id/edit',
                element: <AssemblyFormPage mode="edit" />,
                handle: { title: 'Edit Assembly' },
              },
            ],
          },
          {
            path: '/non-printed-components',
            handle: { title: 'Non-Printed Components' },
            children: [
              { index: true, element: <NonPrintedComponentsPage /> },
              {
                path: 'new',
                element: <NonPrintedComponentFormPage mode="add" />,
                handle: { title: 'Add Non-Printed Component' },
              },
              {
                path: ':id/edit',
                element: <NonPrintedComponentFormPage mode="edit" />,
                handle: { title: 'Edit Non-Printed Component' },
              },
            ],
          },
          {
            path: '/profiles',
            handle: { title: 'Profiles' },
            children: [
              { index: true, element: <ProfilesPage /> },
              {
                path: 'import',
                element: <ProfilesImportPage />,
                handle: { title: 'Import Profiles' },
              },
            ],
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
                handle: { title: 'Connection Secrets' },
                children: [
                  { index: true, element: <ConnectionSecretsPage /> },
                  {
                    path: 'new',
                    element: <ConnectionSecretFormPage />,
                    handle: { title: 'Add Connection Secret' },
                  },
                ],
              },
              {
                path: 'slicing-defaults',
                element: <SlicingDefaultsPage />,
                handle: { title: 'Slicing Defaults' },
              },
            ],
          },
        ],
      },
    ],
  },
]);
