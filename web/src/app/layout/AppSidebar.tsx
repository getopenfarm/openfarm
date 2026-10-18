import {
  ChevronLeftIcon,
  ChevronRightIcon,
  HistoryIcon,
  MoonIcon,
  SettingsIcon,
  SunIcon,
} from 'lucide-react';
import { useTheme } from 'next-themes';
import { Link, useLocation } from 'react-router';
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
  SidebarRail,
  useSidebar,
} from '@/components/ui/sidebar';
import { Logo } from './Logo';
import { type NavItem, navSections } from './nav-config';
import { UserMenu } from './UserMenu';

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

function ThemeButton() {
  const { resolvedTheme, setTheme } = useTheme();
  return (
    <SidebarMenuItem>
      <SidebarMenuButton
        tooltip="Toggle theme"
        onClick={() => setTheme(resolvedTheme === 'dark' ? 'light' : 'dark')}
      >
        <SunIcon className="dark:hidden" />
        <MoonIcon className="hidden dark:block" />
        <span className="dark:hidden">Light</span>
        <span className="hidden dark:inline">Dark</span>
      </SidebarMenuButton>
    </SidebarMenuItem>
  );
}

function CollapseButton() {
  const { isMobile, state, toggleSidebar } = useSidebar();
  if (isMobile) return null;
  const collapsed = state === 'collapsed';
  return (
    <SidebarMenuItem>
      <SidebarMenuButton
        tooltip={collapsed ? 'Expand' : 'Collapse'}
        onClick={toggleSidebar}
      >
        {collapsed ? <ChevronRightIcon /> : <ChevronLeftIcon />}
        <span>Collapse</span>
      </SidebarMenuButton>
    </SidebarMenuItem>
  );
}

export function AppSidebar() {
  return (
    <Sidebar collapsible="icon">
      <SidebarHeader>
        <Logo />
      </SidebarHeader>
      <SidebarContent>
        {navSections.map((section) => (
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
        ))}
      </SidebarContent>
      <SidebarFooter>
        <SidebarMenu>
          <NavLink
            item={{
              title: 'Activity log',
              url: '/activity-log',
              icon: HistoryIcon,
            }}
          />
          <NavLink
            item={{ title: 'Settings', url: '/settings', icon: SettingsIcon }}
          />
          <ThemeButton />
          <CollapseButton />
          <UserMenu />
        </SidebarMenu>
      </SidebarFooter>
      <SidebarRail />
    </Sidebar>
  );
}
