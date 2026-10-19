import { Link, NavLink, Outlet } from 'react-router-dom'

const navLinkClass = ({ isActive }: { isActive: boolean }) =>
  `rounded px-2 py-1 ${isActive ? 'font-semibold underline underline-offset-4' : 'text-muted hover:text-foreground'}`

export default function AppLayout() {
  return (
    <>
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:left-2 focus:top-2 focus:rounded focus:bg-background focus:px-3 focus:py-2"
      >
        Skip to main content
      </a>
      <header className="border-b border-muted/30">
        <div className="mx-auto flex max-w-3xl flex-wrap items-center justify-between gap-4 p-4">
          <Link to="/" className="rounded text-lg font-semibold">
            Audiobook Organizer
          </Link>
          <nav aria-label="Main">
            <ul className="flex flex-wrap gap-2">
              <li>
                <NavLink to="/" end className={navLinkClass}>
                  Dashboard
                </NavLink>
              </li>
              <li>
                <NavLink to="/incoming" className={navLinkClass}>
                  Incoming
                </NavLink>
              </li>
              <li>
                <NavLink to="/people" className={navLinkClass}>
                  People
                </NavLink>
              </li>
              <li>
                <NavLink to="/genres" className={navLinkClass}>
                  Genres
                </NavLink>
              </li>
              <li>
                <NavLink to="/settings" className={navLinkClass}>
                  Settings
                </NavLink>
              </li>
            </ul>
          </nav>
        </div>
      </header>
      <main id="main" tabIndex={-1} className="mx-auto max-w-3xl p-6 focus:outline-none">
        <Outlet />
      </main>
    </>
  )
}
