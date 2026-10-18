import { LogOutIcon, MoreHorizontalIcon, UserIcon } from 'lucide-react';
import { useNavigate } from 'react-router';
import { Avatar, AvatarFallback } from '@/components/ui/avatar';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import {
  SidebarMenuButton,
  SidebarMenuItem,
  useSidebar,
} from '@/components/ui/sidebar';
import { Skeleton } from '@/components/ui/skeleton';
import { useOpenFarmApiEndpointsAuthWhoAmIEndpoint } from '@/lib/api/generated/auth/auth';
import type { WhoAmIResult } from '@/lib/api/generated/endpoints.schemas';
import { useAuth } from '@/lib/auth/useAuth';
import { cn } from '@/lib/utils';

function initials(name: string) {
  return name
    .split(/[\s._@-]+/)
    .filter(Boolean)
    .map((part) => part[0])
    .join('')
    .toUpperCase()
    .slice(0, 2);
}

function UserIdentity({
  name,
  email,
  large,
}: {
  name: string;
  email?: string;
  large?: boolean;
}) {
  return (
    <>
      <Avatar
        className={cn(
          'rounded-lg after:rounded-lg',
          large ? 'size-9' : 'size-8',
        )}
      >
        <AvatarFallback className="rounded-lg border bg-accent text-xs font-semibold text-foreground">
          {initials(name) || <UserIcon className="size-4" />}
        </AvatarFallback>
      </Avatar>
      <div className="grid min-w-0 flex-1 text-left leading-tight group-data-[collapsible=icon]:hidden">
        <span className="truncate text-[13px] font-semibold">{name}</span>
        {email ? (
          <span className="truncate text-[11.5px] text-muted-foreground">
            {email}
          </span>
        ) : null}
      </div>
    </>
  );
}

export function UserMenu() {
  const auth = useAuth();
  const navigate = useNavigate();
  const { isMobile } = useSidebar();
  const query = useOpenFarmApiEndpointsAuthWhoAmIEndpoint();

  const user = query.data?.data as WhoAmIResult | undefined;
  const name = user?.username || 'Account';

  function handleLogout() {
    auth.logout();
    navigate('/login', { replace: true });
  }

  if (query.isLoading) {
    return (
      <SidebarMenuItem>
        <Skeleton className="h-12 w-full group-data-[collapsible=icon]:size-8" />
      </SidebarMenuItem>
    );
  }

  return (
    <SidebarMenuItem className="mt-1 border-t border-sidebar-border pt-2">
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <SidebarMenuButton size="lg" tooltip={name} className="rounded-lg">
            <UserIdentity name={name} email={user?.email} />
            <MoreHorizontalIcon className="ml-auto group-data-[collapsible=icon]:hidden" />
          </SidebarMenuButton>
        </DropdownMenuTrigger>
        <DropdownMenuContent
          side={isMobile ? 'top' : 'right'}
          align="end"
          // 12px sidebar padding + 8px gap, so the popup clears the sidebar edge like akaun's
          sideOffset={20}
          className="min-w-60"
        >
          <DropdownMenuGroup>
            <DropdownMenuLabel className="flex items-center gap-2.5 px-2 py-2 font-normal">
              <UserIdentity name={name} email={user?.email} large />
            </DropdownMenuLabel>
            <DropdownMenuSeparator />
            <DropdownMenuItem
              variant="destructive"
              className="text-[13px]"
              onSelect={handleLogout}
            >
              <LogOutIcon />
              Log out
            </DropdownMenuItem>
          </DropdownMenuGroup>
        </DropdownMenuContent>
      </DropdownMenu>
    </SidebarMenuItem>
  );
}
