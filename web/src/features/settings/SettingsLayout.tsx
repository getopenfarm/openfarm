import { KeyRoundIcon, PackageIcon } from 'lucide-react';
import { NavLink, Outlet } from 'react-router';
import { cn } from '@/lib/utils';

const sections = [
  {
    title: 'Connection credentials',
    to: 'connection-secrets',
    icon: KeyRoundIcon,
  },
  { title: 'Slicing defaults', to: 'slicing-defaults', icon: PackageIcon },
];

export function SettingsLayout() {
  return (
    <div className="flex flex-1 flex-col gap-6 md:flex-row">
      <nav
        aria-label="Settings"
        className="flex shrink-0 gap-1 overflow-x-auto md:w-52 md:flex-col"
      >
        {sections.map(({ title, to, icon: Icon }) => (
          <NavLink
            key={to}
            to={to}
            className={({ isActive }) =>
              cn(
                'flex items-center gap-2.5 whitespace-nowrap rounded-lg px-[11px] py-2 text-[13.5px] font-medium text-muted-foreground transition-colors hover:bg-sidebar-accent hover:text-foreground',
                isActive && 'bg-sidebar-accent text-foreground',
              )
            }
          >
            <Icon className="size-4 shrink-0" aria-hidden="true" />
            {title}
          </NavLink>
        ))}
      </nav>
      <div className="flex min-w-0 flex-1 flex-col">
        <Outlet />
      </div>
    </div>
  );
}
