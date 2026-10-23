import { createBrowserRouter, Navigate } from 'react-router';
import { ActivityLogPage } from '../features/activity-log/ActivityLogPage';
import { AssembliesPage } from '../features/assemblies/AssembliesPage';
import { AssemblyDetailPage } from '../features/assemblies/AssemblyDetailPage';
import { AssemblyFormPage } from '../features/assemblies/AssemblyFormPage';
import { LoginPage } from '../features/auth/LoginPage';
import { ConnectionSecretFormPage } from '../features/connection-secrets/ConnectionSecretFormPage';
import { ConnectionSecretsPage } from '../features/connection-secrets/ConnectionSecretsPage';
import { CostCatalogPage } from '../features/cost-catalog/CostCatalogPage';
import { CostItemFormPage } from '../features/cost-catalog/CostItemFormPage';
import { LegacyCostCatalogRedirect } from '../features/cost-catalog/LegacyCostCatalogRedirect';
import { DashboardPage } from '../features/dashboard/DashboardPage';
import { FilamentDetailPage } from '../features/filament/FilamentDetailPage';
import { FilamentFormPage } from '../features/filament/FilamentFormPage';
import { FilamentPage } from '../features/filament/FilamentPage';
import { MaintenancePage } from '../features/maintenance/MaintenancePage';
import { PartDetailPage } from '../features/parts/PartDetailPage';
import { PartFormPage } from '../features/parts/PartFormPage';
import { PartsPage } from '../features/parts/PartsPage';
import { PrinterDetailPage } from '../features/printers/PrinterDetailPage';
import { PrinterFormPage } from '../features/printers/PrinterFormPage';
import { PrintersPage } from '../features/printers/PrintersPage';
import { NewProductionRunPage } from '../features/production-runs/NewProductionRunPage';
import { PrintJobDetailPage } from '../features/production-runs/PrintJobDetailPage';
import { ProductionRunDetailPage } from '../features/production-runs/ProductionRunDetailPage';
import { ProductionRunEditPage } from '../features/production-runs/ProductionRunEditPage';
import { ProductionRunsPage } from '../features/production-runs/ProductionRunsPage';
import { QueuePage } from '../features/production-runs/QueuePage';
import { ProfileDetailPage } from '../features/profiles/ProfileDetailPage';
import { ProfilesImportPage } from '../features/profiles/ProfilesImportPage';
import { ProfilesPage } from '../features/profiles/ProfilesPage';
import { SettingsLayout } from '../features/settings/SettingsLayout';
import { SlicingDefaultsPage } from '../features/settings/SlicingDefaultsPage';
import { SpoolDetailPage } from '../features/spools/SpoolDetailPage';
import { SpoolFormPage } from '../features/spools/SpoolFormPage';
import { StatsPage } from '../features/stats/StatsPage';
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
              {
                index: true,
                element: <PrintersPage />,
              },
              {
                path: 'new',
                element: <PrinterFormPage mode="add" />,
                handle: { title: 'Add printer' },
              },
              {
                path: ':id',
                element: <PrinterDetailPage />,
                handle: { title: 'Printer detail' },
              },
              {
                path: ':id/edit',
                element: <PrinterFormPage mode="edit" />,
                handle: { title: 'Edit printer' },
              },
            ],
          },
          {
            path: '/jobs',
            element: <QueuePage />,
            handle: { title: 'Jobs', fillViewport: true },
          },
          {
            path: '/jobs/:id',
            element: <PrintJobDetailPage />,
            handle: { title: 'Print job' },
          },
          {
            path: '/stats',
            element: <StatsPage />,
            handle: { title: 'Stats' },
          },
          {
            path: '/production-runs',
            handle: { title: 'Production runs' },
            children: [
              {
                index: true,
                element: <ProductionRunsPage />,
              },
              {
                path: 'new',
                element: <NewProductionRunPage />,
                handle: { title: 'Add production run' },
              },
              {
                path: ':id',
                element: <ProductionRunDetailPage />,
                handle: { title: 'Production run' },
              },
              {
                path: ':id/edit',
                element: <ProductionRunEditPage />,
                handle: { title: 'Edit production run' },
              },
            ],
          },
          {
            path: '/filament',
            handle: { title: 'Filament' },
            children: [
              {
                index: true,
                element: <FilamentPage />,
              },
              {
                path: 'new',
                element: <FilamentFormPage mode="add" />,
                handle: { title: 'Add filament' },
              },
              // Static, so they rank above ':id'; no titled 'spools' parent, whose crumb would link to a filament named "spools".
              {
                path: 'spools/new',
                element: <SpoolFormPage />,
                handle: { title: 'Add spools' },
              },
              {
                path: 'spools/:id',
                element: <SpoolDetailPage />,
                handle: { title: 'Spool' },
              },
              {
                path: ':id',
                handle: { title: 'Filament detail' },
                children: [
                  {
                    index: true,
                    element: <FilamentDetailPage />,
                  },
                  {
                    path: 'edit',
                    element: <FilamentFormPage mode="edit" />,
                    handle: { title: 'Edit filament' },
                  },
                ],
              },
            ],
          },
          {
            path: '/parts',
            handle: { title: 'Parts' },
            children: [
              {
                index: true,
                element: <PartsPage />,
              },
              {
                path: 'new',
                element: <PartFormPage />,
                handle: { title: 'Add part' },
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
              {
                index: true,
                element: <AssembliesPage />,
              },
              {
                path: 'new',
                element: <AssemblyFormPage />,
                handle: { title: 'Add assembly' },
              },
              {
                path: ':id',
                element: <AssemblyDetailPage />,
                handle: { title: 'Assembly' },
              },
              {
                path: ':id/edit',
                element: <Navigate to=".." relative="path" replace />,
              },
            ],
          },
          {
            path: '/non-printed-components/*',
            element: <LegacyCostCatalogRedirect />,
          },
          {
            path: '/cost-catalog',
            handle: { title: 'Cost catalog' },
            children: [
              {
                index: true,
                element: <CostCatalogPage />,
              },
              {
                path: 'new',
                element: <CostItemFormPage mode="add" />,
                handle: { title: 'Add cost item' },
              },
              {
                path: ':id',
                element: <CostItemFormPage mode="edit" />,
                handle: { title: 'Cost item' },
              },
              {
                path: ':id/edit',
                element: <Navigate to=".." relative="path" replace />,
              },
            ],
          },
          {
            path: '/profiles',
            handle: { title: 'Profiles' },
            children: [
              {
                index: true,
                element: <ProfilesPage />,
              },
              {
                path: 'import',
                element: <ProfilesImportPage />,
                handle: { title: 'Import profiles' },
              },
              {
                path: ':type/:id',
                element: <ProfileDetailPage />,
                handle: { title: 'Profile' },
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
            handle: { title: 'Activity log' },
          },
          {
            path: '/settings',
            element: <SettingsLayout />,
            handle: { title: 'Settings' },
            children: [
              {
                index: true,
                element: <Navigate to="connection-secrets" replace />,
              },
              {
                path: 'connection-secrets',
                handle: { title: 'Connection credentials' },
                children: [
                  {
                    index: true,
                    element: <ConnectionSecretsPage />,
                  },
                  {
                    path: 'new',
                    element: <ConnectionSecretFormPage />,
                    handle: { title: 'Add credential' },
                  },
                ],
              },
              {
                path: 'slicing-defaults',
                element: <SlicingDefaultsPage />,
                handle: { title: 'Slicing defaults' },
              },
            ],
          },
        ],
      },
    ],
  },
]);
