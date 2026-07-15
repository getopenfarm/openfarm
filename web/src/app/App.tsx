import { QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from 'react-router/dom';
import { ThemeProvider } from '@/components/theme-provider';
import { Toaster } from '@/components/ui/sonner';
import { queryClient } from '@/lib/api/queryClient';
import { AuthProvider } from '@/lib/auth/AuthProvider';
import { PreferencesProvider } from '@/lib/preferences/usePreferences';
import { router } from './routes';

function App() {
  return (
    <ThemeProvider attribute="class" defaultTheme="system" enableSystem>
      <PreferencesProvider>
        <QueryClientProvider client={queryClient}>
          <AuthProvider>
            <RouterProvider router={router} />
            <Toaster />
          </AuthProvider>
        </QueryClientProvider>
      </PreferencesProvider>
    </ThemeProvider>
  );
}

export default App;
