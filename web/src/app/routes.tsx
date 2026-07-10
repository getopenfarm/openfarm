import { createBrowserRouter } from 'react-router';
import { DashboardPage } from '../features/dashboard/DashboardPage';

export const router = createBrowserRouter([
  {
    path: '/',
    element: <DashboardPage />,
  },
]);
