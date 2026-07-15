import {
  HistoryIcon,
  LogOutIcon,
  PanelLeftCloseIcon,
  PanelLeftIcon,
  SettingsIcon,
  UserIcon,
} from 'lucide-react';
import { Link, useLocation, useNavigate } from 'react-router';
import { Avatar, AvatarFallback } from '@/components/ui/avatar';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  useSidebar,
} from '@/components/ui/sidebar';
import { useAuth } from '@/lib/auth/useAuth';
import { usePreferences } from '@/lib/preferences/usePreferences';
import { Logo } from './Logo';
import { flatNavItems, type NavItem, navSections } from './nav-config';

function NavLink({ item }: { item: NavItem }) {
  const { pathname } = useLocation();
  const isActive =
    item.url === '/' ? pathname === '/' : pathname.startsWith(item.url);

  return (
    <SidebarMenuItem>
      <SidebarMenuButton asChild tooltip={item.title} isActive={isActive}>
        <Link to={item.url}>
          <item.icon />
          <span>{item.title}</span>
        </Link>
      </SidebarMenuButton>
    </SidebarMenuItem>
  );
}

export function AppSidebar() {
  const auth = useAuth();
  const navigate = useNavigate();
  const { toggleSidebar, state } = useSidebar();
  const {
    preferences: { sidebarVariant },
  } = usePreferences();

  function handleLogout() {
    auth.logout();
    navigate('/login', { replace: true });
  }

  return (
    <Sidebar collapsible="icon">
      <SidebarHeader>
        <Logo />
      </SidebarHeader>
      <SidebarContent>
        {sidebarVariant === 'flat' || sidebarVariant === 'rail' ? (
          <SidebarGroup>
            <SidebarGroupContent>
              <SidebarMenu>
                {flatNavItems.map((item) => (
                  <NavLink key={item.url} item={item} />
                ))}
              </SidebarMenu>
            </SidebarGroupContent>
          </SidebarGroup>
        ) : (
          navSections.map((section) => (
            <SidebarGroup key={section.label}>
              <SidebarGroupLabel>{section.label}</SidebarGroupLabel>
              <SidebarGroupContent>
                <SidebarMenu>
                  {section.items.map((item) => (
                    <NavLink key={item.url} item={item} />
                  ))}
                </SidebarMenu>
              </SidebarGroupContent>
            </SidebarGroup>
          ))
        )}
      </SidebarContent>
      <SidebarFooter>
        <SidebarMenu>
          <NavLink
            item={{
              title: 'Activity Log',
              url: '/activity-log',
              icon: HistoryIcon,
            }}
          />
          <NavLink
            item={{ title: 'Settings', url: '/settings', icon: SettingsIcon }}
          />
          <SidebarMenuItem>
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <SidebarMenuButton tooltip="Account">
                  <Avatar className="size-6">
                    <AvatarFallback>
                      <UserIcon className="size-4" />
                    </AvatarFallback>
                  </Avatar>
                  <span>Account</span>
                </SidebarMenuButton>
              </DropdownMenuTrigger>
              <DropdownMenuContent side="right" align="end">
                <DropdownMenuItem onSelect={handleLogout}>
                  <LogOutIcon />
                  Log out
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </SidebarMenuItem>
          {sidebarVariant === 'rail' && (
            <SidebarMenuItem>
              <SidebarMenuButton
                tooltip={state === 'collapsed' ? 'Expand' : 'Collapse'}
                onClick={toggleSidebar}
              >
                {state === 'collapsed' ? (
                  <PanelLeftIcon />
                ) : (
                  <PanelLeftCloseIcon />
                )}
                <span>{state === 'collapsed' ? 'Expand' : 'Collapse'}</span>
              </SidebarMenuButton>
            </SidebarMenuItem>
          )}
        </SidebarMenu>
      </SidebarFooter>
    </Sidebar>
  );
}
