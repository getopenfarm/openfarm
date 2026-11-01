import type { ComponentProps } from 'react';
import { Button } from '@/components/ui/button';
import { useAllowed } from '@/lib/auth/AccessContext';
export function PermissionButton({
  permission,
  ...props
}: ComponentProps<typeof Button> & { permission?: string }) {
  const allowed = useAllowed(permission);
  return allowed ? <Button {...props} /> : null;
}
