import {
  ChevronLeftIcon,
  ChevronRightIcon,
  MoonIcon,
  SettingsIcon,
  SunIcon,
} from 'lucide-react';
import { useTheme } from 'next-themes';
import { useContext } from 'react';
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
import { AccessContext, useAllowed } from '@/lib/auth/AccessContext';
import { routePermission, settingsPath } from '@/lib/auth/permissions';
import { Logo } from './Logo';
import {
  adminNavSection,
  type NavItem,
  type NavSection,
  navSections,
} from './nav-config';
import { UserMenu } from './UserMenu';

function NavLink({
  item,
  match = item.url,
}: {
  item: NavItem;
  match?: string;
}) {
  const { pathname } = useLocation();
  const allowed = useAllowed(routePermission(item.url) ?? undefined);
  if (!allowed) return null;
  const isActive = pathname === match || pathname.startsWith(`${match}/`);
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
      <SidebarMenuButton tooltip="Toggle sidebar" onClick={toggleSidebar}>
        {collapsed ? <ChevronRightIcon /> : <ChevronLeftIcon />}
        <span>Collapse</span>
      </SidebarMenuButton>
    </SidebarMenuItem>
  );
}

function NavGroup({
  section,
  className,
}: {
  section: NavSection;
  className?: string;
}) {
  return (
    <SidebarGroup className={className}>
      <SidebarGroupLabel>{section.label}</SidebarGroupLabel>
      <SidebarGroupContent>
        <SidebarMenu>
          {section.items.map((item) => (
            <NavLink key={item.url} item={item} />
          ))}
        </SidebarMenu>
      </SidebarGroupContent>
    </SidebarGroup>
  );
}

export function AppSidebar() {
  const access = useContext(AccessContext);
  const can = (key: string | null) =>
    access === undefined || key === null || access.has(key);
  const allowed = (section: NavSection) => ({
    ...section,
    items: section.items.filter((item) => can(routePermission(item.url))),
  });
  const sections = navSections
    .map(allowed)
    .filter((section) => section.items.length > 0);
  const admin = allowed(adminNavSection);
  const settings = settingsPath(can);
  return (
    <Sidebar collapsible="icon">
      <SidebarHeader>
        <Logo />
      </SidebarHeader>
      <SidebarContent>
        {sections.map((section) => (
          <NavGroup key={section.label} section={section} />
        ))}
      </SidebarContent>
      <SidebarFooter>
        {admin.items.length > 0 && <NavGroup section={admin} className="p-0" />}
        <SidebarMenu>
          {settings && (
            <NavLink
              item={{ title: 'Settings', url: settings, icon: SettingsIcon }}
              match="/settings"
            />
          )}
          <ThemeButton />
          <CollapseButton />
          <UserMenu />
        </SidebarMenu>
      </SidebarFooter>
      <SidebarRail />
    </Sidebar>
  );
}
