import { useContext } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router';
import {
  SideNavPage,
  sideNavItemClass,
} from '@/components/side-nav-page/SideNavPage';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { AccessContext, AdministratorContext } from '@/lib/auth/AccessContext';
import { routePermission } from '@/lib/auth/permissions';

const sections = [
  { title: 'General', to: 'general' },
  { title: 'Server', to: 'server', administrator: true },
  {
    title: 'AI assistants',
    to: 'integrations/ai-assistants',
    administrator: true,
  },
  { title: 'Printer costs', to: 'printer-costs' },
  { title: 'Fees', to: 'fees' },
  { title: 'Slicing defaults', to: 'slicing-defaults' },
];

export function SettingsLayout() {
  const access = useContext(AccessContext);
  const administrator = useContext(AdministratorContext);
  const { pathname } = useLocation();
  const current = sections.find(
    (s) =>
      pathname === `/settings/${s.to}` ||
      pathname.startsWith(`/settings/${s.to}/`),
  );
  const readOnly =
    access !== undefined &&
    current !== undefined &&
    !current.administrator &&
    !access.has(`settings:${current.to}:update`);
  return (
    <SideNavPage
      title="Settings"
      description="Configure your farm"
      navLabel="Settings"
      nav={sections
        .filter(
          (s) =>
            (!s.administrator || administrator !== false) &&
            (access === undefined ||
              access.has(routePermission(`/settings/${s.to}`) ?? '')),
        )
        .map(({ title, to }) => (
          <NavLink
            key={to}
            to={to}
            className={({ isActive }) => sideNavItemClass(isActive)}
          >
            {title}
          </NavLink>
        ))}
    >
      {readOnly && (
        <Alert className="mb-4 max-w-[640px]" role="status">
          <AlertDescription>
            You can see these settings but not change them. An administrator can
            give you the Settings › {current?.title} › Change permission on
            Users &amp; groups.
          </AlertDescription>
        </Alert>
      )}
      {/* Greys the controls out only; the server refuses the saves anyway. */}
      <fieldset disabled={readOnly} className="contents">
        <Outlet />
      </fieldset>
    </SideNavPage>
  );
}
