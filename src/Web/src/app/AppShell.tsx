import { useState } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { LogOut, Menu, ShieldCheck, X } from 'lucide-react'
import clsx from 'clsx'
import { navigationSections } from './navigation'
import { hasPermission, useLogout, useSession } from '@/features/auth/session'
import { Button } from '@/components/ui/Button'
import { LanguageSwitcher, ThemeToggle } from '@/components/PreferencesControls'
import { LoadingState } from '@/components/ui/Surface'

export function AppShell() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { data: user, isLoading } = useSession()
  const logout = useLogout()
  const [mobileNavOpen, setMobileNavOpen] = useState(false)

  if (isLoading || !user) {
    return <LoadingState label={t('common.loading')} />
  }

  const visibleSections = navigationSections
    .map((section) => ({
      ...section,
      items: section.items.filter((item) => !item.permission || hasPermission(user, item.permission)),
    }))
    .filter((section) => section.items.length > 0)

  const handleSignOut = async () => {
    try {
      await logout.mutateAsync()
    } catch {
      // The local session is cleared either way; a failed call must not trap the user in the app.
    }

    navigate('/login', { replace: true })
  }

  return (
    <div className="min-h-dvh bg-canvas">
      <a href="#main-content" className="skip-link">
        {t('common.skipToContent')}
      </a>

      <header className="sticky top-0 z-30 flex h-14 items-center gap-3 border-b border-subtle bg-raised px-3 sm:px-4">
        <Button
          variant="ghost"
          size="sm"
          className="lg:hidden"
          onClick={() => setMobileNavOpen((open) => !open)}
          aria-expanded={mobileNavOpen}
          aria-controls="app-navigation"
          aria-label={mobileNavOpen ? t('common.closeMenu') : t('common.openMenu')}
        >
          {mobileNavOpen ? <X aria-hidden className="size-4" /> : <Menu aria-hidden className="size-4" />}
        </Button>

        <div className="flex items-center gap-2">
          <span className="grid size-7 place-items-center rounded-md bg-teal-700 text-white">
            <ShieldCheck aria-hidden className="size-4" />
          </span>
          <div className="leading-tight">
            <p className="text-sm font-semibold text-ink">{t('common.appName')}</p>
            <p className="hidden text-[11px] text-ink-muted sm:block">{user.tenantName}</p>
          </div>
        </div>

        <div className="ml-auto flex items-center gap-2">
          <div className="hidden sm:block">
            <LanguageSwitcher />
          </div>
          <ThemeToggle />

          <div className="flex items-center gap-2 border-l border-subtle pl-2">
            <span
              className="grid size-8 place-items-center rounded-full bg-sunken text-xs font-semibold text-ink"
              aria-hidden
            >
              {user.initials}
            </span>
            <div className="hidden leading-tight md:block">
              <p className="text-xs font-medium text-ink">{user.fullName}</p>
              <p className="text-[11px] text-ink-muted">{user.roles.join(', ')}</p>
            </div>
            <Button
              variant="ghost"
              size="sm"
              onClick={handleSignOut}
              loading={logout.isPending}
              aria-label={t('common.signOut')}
              title={t('common.signOut')}
            >
              <LogOut aria-hidden className="size-4" />
            </Button>
          </div>
        </div>
      </header>

      <div className="flex">
        <nav
          id="app-navigation"
          aria-label={t('nav.sectionWork')}
          className={clsx(
            'w-60 shrink-0 border-r border-subtle bg-raised px-2 py-3',
            'lg:sticky lg:top-14 lg:block lg:h-[calc(100dvh-3.5rem)]',
            mobileNavOpen
              ? 'fixed inset-x-0 top-14 z-20 block h-[calc(100dvh-3.5rem)] w-full overflow-y-auto'
              : 'hidden',
          )}
        >
          {visibleSections.map((section) => (
            <div key={section.labelKey} className="mb-4">
              <p className="px-2 pb-1 text-[11px] font-semibold uppercase tracking-wide text-ink-muted">
                {t(section.labelKey)}
              </p>
              <ul className="space-y-0.5">
                {section.items.map((item) => (
                  <li key={item.to}>
                    <NavLink
                      to={item.to}
                      onClick={() => setMobileNavOpen(false)}
                      className={({ isActive }) =>
                        clsx(
                          'flex items-center gap-2.5 rounded-md px-2 py-1.5 text-sm transition-colors',
                          isActive
                            ? 'bg-teal-50 font-medium text-teal-800 dark:bg-teal-950 dark:text-teal-200'
                            : 'text-ink-secondary hover:bg-sunken hover:text-ink',
                        )
                      }
                    >
                      <item.icon aria-hidden className="size-4" />
                      {t(item.labelKey)}
                    </NavLink>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </nav>

        <main id="main-content" className="min-w-0 flex-1 px-4 py-5 sm:px-6">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
