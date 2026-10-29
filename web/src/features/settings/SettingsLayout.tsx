import { NavLink, Outlet } from 'react-router';
import { cn } from '@/lib/utils';

const sections = [
  { title: 'General', to: 'general' },
  { title: 'Printer costs', to: 'printer-costs' },
  { title: 'Slicing defaults', to: 'slicing-defaults' },
];

export function SettingsLayout() {
  return (
    // -m-4 cancels AppShell's padding so the header rule and nav divider reach the edges.
    <div className="-m-4 flex flex-1 flex-col">
      <header className="border-b px-7 pt-5 pb-4">
        <h1 className="font-heading text-[22px] leading-tight font-semibold tracking-[-0.02em]">
          Settings
        </h1>
        <p className="mt-[3px] text-[13px] text-muted-foreground">
          Configure your farm
        </p>
      </header>
      <div className="flex min-h-0 flex-1 flex-col md:flex-row">
        <nav
          aria-label="Settings"
          className="flex shrink-0 gap-[3px] overflow-x-auto border-b px-3.5 py-[18px] md:w-[200px] md:flex-col md:border-r md:border-b-0"
        >
          {sections.map(({ title, to }) => (
            <NavLink
              key={to}
              to={to}
              className={({ isActive }) =>
                cn(
                  'whitespace-nowrap rounded-lg px-[11px] py-2 text-[13.5px] font-medium text-muted-foreground transition-colors hover:bg-accent hover:text-foreground',
                  isActive && 'bg-accent text-foreground',
                )
              }
            >
              {title}
            </NavLink>
          ))}
        </nav>
        <div className="flex min-w-0 flex-1 flex-col overflow-y-auto px-7 pt-6 pb-12">
          <Outlet />
        </div>
      </div>
    </div>
  );
}
