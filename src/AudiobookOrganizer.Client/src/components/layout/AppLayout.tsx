import { ChevronDown } from 'lucide-react'
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '../ui/dropdown-menu'

const linkClass = 'rounded px-2 py-1'
const activeClass = 'font-semibold underline underline-offset-4'
const idleClass = 'text-muted hover:text-foreground'

const navLinkClass = ({ isActive }: { isActive: boolean }) => `${linkClass} ${isActive ? activeClass : idleClass}`

const mainLinks = [
  { to: '/', label: 'Review' },
  { to: '/dashboard', label: 'Dashboard' },
  { to: '/incoming', label: 'Incoming' },
  { to: '/preview', label: 'Preview' },
  { to: '/jobs', label: 'Jobs' },
]

const moreLinks = [
  { to: '/people', label: 'People' },
  { to: '/genres', label: 'Genres' },
  { to: '/settings', label: 'Settings' },
]

export default function AppLayout() {
  const { pathname } = useLocation()
  const onMorePage = moreLinks.some((l) => pathname === l.to || pathname.startsWith(`${l.to}/`))

  return (
    <>
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:left-2 focus:top-2 focus:rounded focus:bg-background focus:px-3 focus:py-2"
      >
        Skip to main content
      </a>
      <header className="border-b border-muted/30">
        <div className="flex flex-wrap items-center justify-between gap-4 px-6 py-4">
          <Link to="/" className="rounded text-lg font-semibold">
            Audiobook Organizer
          </Link>
          <nav aria-label="Main">
            <ul className="flex flex-wrap items-center gap-2">
              {mainLinks.map((l) => (
                <li key={l.to}>
                  <NavLink to={l.to} end={l.to === '/'} className={navLinkClass}>
                    {l.label}
                  </NavLink>
                </li>
              ))}
              <li>
                <DropdownMenu>
                  <DropdownMenuTrigger
                    aria-current={onMorePage ? 'true' : undefined}
                    className={`inline-flex items-center gap-1 ${linkClass} ${onMorePage ? activeClass : idleClass}`}
                  >
                    More
                    <ChevronDown aria-hidden="true" className="size-4" />
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end">
                    {moreLinks.map((l) => (
                      <DropdownMenuItem key={l.to} asChild>
                        <NavLink to={l.to} className={({ isActive }) => (isActive ? 'font-semibold' : undefined)}>
                          {l.label}
                        </NavLink>
                      </DropdownMenuItem>
                    ))}
                  </DropdownMenuContent>
                </DropdownMenu>
              </li>
            </ul>
          </nav>
        </div>
      </header>
      <main id="main" tabIndex={-1} className="mx-auto max-w-7xl p-6 focus:outline-none">
        <Outlet />
      </main>
    </>
  )
}
