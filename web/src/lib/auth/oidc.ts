import { useQuery } from '@tanstack/react-query';
import { apiUrl } from '@/lib/api/apiUrl';

export interface LoginProviders {
  oidcEnabled: boolean;
  displayName: string;
  internalAuthenticationEnabled: boolean;
}

export function useLoginProviders() {
  return useQuery({
    queryKey: ['auth', 'providers'],
    queryFn: async (): Promise<LoginProviders> => {
      const response = await fetch(apiUrl('/api/v1/auth/providers'), {
        cache: 'no-store',
      });
      if (!response.ok) throw response;
      return response.json();
    },
    retry: false,
  });
}

export function localReturnTo(value: string | null): string {
  return value &&
    value.length <= 2048 &&
    value.startsWith('/') &&
    !value.startsWith('//') &&
    !Array.from(value).some(
      (char) =>
        char === '\\' || char.charCodeAt(0) < 32 || char.charCodeAt(0) === 127,
    ) &&
    !/^\/(api|auth\/complete)/i.test(value)
    ? value
    : '/';
}
