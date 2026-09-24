import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { LogOut, ShieldCheck } from 'lucide-react'
import clsx from 'clsx'
import { useLogout, useSession } from '@/features/auth/session'
import { Button } from '@/components/ui/Button'
import { LanguageSwitcher, ThemeToggle } from '@/components/PreferencesControls'
import { LoadingState } from '@/components/ui/Surface'

/**
 * The external portal's own shell.
 *
 * It deliberately shares nothing with {@link AppShell} but the design system. There is no internal
 * navigation to hide, no notification bell, no department or role on the header — a supplier is a
 * guest, and a guest should not be able to tell from the chrome what the organisation's internal
 * screens even are. What it does show is whose name the visitor is signed in under, because a
 * contact who works with several clients needs to know which portal this is.
 */
export function PortalShell() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { data: user, isLoading } = useSession()
  const logout = useLogout()

  if (isLoading || !user) {
    return <LoadingState label={t('common.loading')} />
  }

  const handleSignOut = async () => {
    try {
      await logout.mutateAsync()
    } catch {
      // The local session is cleared either way; a failed call must not trap the user in the portal.
    }

    navigate('/login', { replace: true })
  }

  return (
    <div className="min-h-screen bg-canvas">
      <a href="#portal-content" className="skip-link">
        {t('common.skipToContent')}
      </a>

      <header className="sticky top-0 z-30 border-b border-subtle bg-raised">
        <div className="mx-auto flex h-14 max-w-5xl items-center gap-3 px-4">
          <div className="flex items-center gap-2">
            <span className="grid size-7 place-items-center rounded-md bg-graphite-800 text-white">
              <ShieldCheck aria-hidden className="size-4" />
            </span>
            <div className="leading-tight">
              <p className="text-sm font-semibold text-ink">{t('portal.title')}</p>
              <p className="text-[11px] text-ink-muted">{user.tenantName}</p>
            </div>
          </div>

          <div className="ml-auto flex items-center gap-2">
            <div className="hidden sm:block">
              <LanguageSwitcher />
            </div>
            <ThemeToggle />

            <div className="flex items-center gap-2 border-l border-subtle pl-2">
              <div className="leading-tight">
                {/* A organização fica visível em qualquer ecrã: é o que diz ao visitante de que
                    portal se trata, e um contacto que trabalha com vários clientes precisa disso
                    tanto no telemóvel como no computador. O nome próprio é que pode esperar. */}
                <p className="text-xs font-medium text-ink">{user.externalOrganisation ?? user.fullName}</p>
                <p className="hidden text-[11px] text-ink-muted sm:block">{user.fullName}</p>
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
        </div>

        <nav aria-label={t('portal.title')} className="mx-auto max-w-5xl px-4">
          <ul className="flex gap-1 pb-2">
            <li>
              <NavLink
                to="/portal"
                end
                className={({ isActive }) =>
                  clsx(
                    'inline-flex rounded-md px-3 py-1.5 text-sm transition-colors',
                    isActive
                      ? 'bg-teal-50 font-medium text-teal-800 dark:bg-teal-950 dark:text-teal-200'
                      : 'text-ink-secondary hover:bg-sunken hover:text-ink',
                  )
                }
              >
                {t('portal.nav.submissions')}
              </NavLink>
            </li>
          </ul>
        </nav>
      </header>

      <main id="portal-content" className="mx-auto max-w-5xl px-4 py-6">
        <Outlet />
      </main>

      <footer className="mx-auto max-w-5xl px-4 pb-8">
        <p className="text-[11px] text-ink-muted">{t('portal.footer')}</p>
      </footer>
    </div>
  )
}
