import type { ComponentProps } from 'react';
import { DropdownMenuItem } from '@/components/ui/dropdown-menu';
import { useAllowed } from '@/lib/auth/AccessContext';
export function PermissionMenuItem({
  permission,
  ...props
}: ComponentProps<typeof DropdownMenuItem> & { permission?: string }) {
  const allowed = useAllowed(permission);
  return allowed ? <DropdownMenuItem {...props} /> : null;
}
