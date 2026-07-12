import { QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from 'react-router/dom';
import { Toaster } from '@/components/ui/sonner';
import { queryClient } from '@/lib/api/queryClient';
import { AuthProvider } from '@/lib/auth/AuthProvider';
import { router } from './routes';

function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <RouterProvider router={router} />
        <Toaster />
      </AuthProvider>
    </QueryClientProvider>
  );
}

export default App;
