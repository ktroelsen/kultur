import { Bookmark, Heart, LogOut, Sparkles, UserRound, type LucideIcon } from 'lucide-react';
import { NavLink, Outlet } from 'react-router';
import { useAuth } from '../auth';
import Logo from './Logo';

const nav: { to: string; label: string; icon: LucideIcon }[] = [
  { to: '/', label: 'Siden sidst', icon: Sparkles },
  { to: '/for-you', label: 'Til dig', icon: Heart },
  { to: '/saved', label: 'Gemt', icon: Bookmark },
  { to: '/profile', label: 'Dig', icon: UserRound },
];

// Mobile: top bar + bottom tab bar. Desktop: the same links in the top bar.
export default function Layout() {
  const { logout } = useAuth();

  return (
    <div className="flex h-full flex-col">
      <header className="border-b border-line bg-surface pt-[env(safe-area-inset-top)]">
        <div className="mx-auto flex max-w-3xl items-center justify-between px-4 py-3">
          <Logo />
          <nav className="hidden items-center gap-1 md:flex">
            {nav.map(({ to, label, icon: Icon }) => (
              <NavLink
                key={to}
                to={to}
                end
                className={({ isActive }) =>
                  `flex items-center gap-2 rounded-lg px-3 py-2 text-sm transition ${
                    isActive ? 'bg-accent/10 text-accent' : 'text-muted hover:bg-surface-2 hover:text-fg'
                  }`
                }
              >
                <Icon className="h-4 w-4" />
                {label}
              </NavLink>
            ))}
          </nav>
          <button onClick={logout} aria-label="Log ud" title="Log ud" className="rounded-lg p-2 text-muted hover:text-fg">
            <LogOut className="h-5 w-5" />
          </button>
        </div>
      </header>

      <main className="flex-1 overflow-y-auto pb-[calc(5rem+env(safe-area-inset-bottom))] md:pb-10">
        <div className="mx-auto max-w-3xl px-4 py-6 md:py-10">
          <Outlet />
        </div>
      </main>

      <nav className="fixed inset-x-0 bottom-0 grid grid-cols-4 border-t border-line bg-surface pb-[env(safe-area-inset-bottom)] md:hidden">
        {nav.map(({ to, label, icon: Icon }) => (
          <NavLink
            key={to}
            to={to}
            end
            className={({ isActive }) => `flex flex-col items-center gap-1 py-2.5 text-xs ${isActive ? 'text-accent' : 'text-muted'}`}
          >
            <Icon className="h-5 w-5" />
            {label}
          </NavLink>
        ))}
      </nav>
    </div>
  );
}
