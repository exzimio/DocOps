import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { BrainCircuit, FileSearch, GitBranch, PlugZap, ScrollText, ShieldCheck, Sparkles } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Badge, Card, CardBody, TableScroller } from '@/components/ui/Surface'
import { LanguageSwitcher, ThemeToggle } from '@/components/PreferencesControls'
import { demoAccounts, demoPassword } from '@/features/auth/demoAccounts'

const features = [
  { key: 'documentIntelligence', icon: BrainCircuit },
  { key: 'workflowAutomation', icon: GitBranch },
  { key: 'secureSearch', icon: FileSearch },
  { key: 'aiProcessing', icon: Sparkles },
  { key: 'auditCompliance', icon: ScrollText },
  { key: 'integrations', icon: PlugZap },
] as const

export function LandingPage() {
  const { t } = useTranslation()

  return (
    <div className="min-h-screen bg-canvas">
      <header className="border-b border-subtle bg-raised">
        <div className="mx-auto flex h-14 max-w-6xl items-center gap-3 px-4">
          <span className="grid size-7 place-items-center rounded-md bg-teal-700 text-white">
            <ShieldCheck aria-hidden className="size-4" />
          </span>
          <p className="text-sm font-semibold text-ink">{t('common.appName')}</p>
          <div className="ml-auto flex items-center gap-2">
            <LanguageSwitcher />
            <ThemeToggle />
            <Link to="/login">
              <Button size="sm">{t('common.signIn')}</Button>
            </Link>
          </div>
        </div>
      </header>

      <main>
        <section className="mx-auto max-w-6xl px-4 py-14 sm:py-20">
          <Badge tone="teal">{t('common.independentProject')}</Badge>
          <h1 className="mt-4 max-w-3xl text-balance text-3xl font-semibold tracking-tight text-ink sm:text-4xl">
            {t('landing.heroTitle')}
          </h1>
          <p className="mt-3 max-w-2xl text-balance text-base text-ink-secondary sm:text-lg">
            {t('landing.heroSubtitle')}
          </p>
          <p className="mt-4 max-w-2xl text-sm text-ink-muted">{t('landing.heroBody')}</p>

          <div className="mt-7 flex flex-wrap gap-3">
            <Link to="/login">
              <Button size="lg">{t('landing.launchDemo')}</Button>
            </Link>
          </div>
        </section>

        <section className="border-y border-subtle bg-raised">
          <div className="mx-auto max-w-6xl px-4 py-12">
            <h2 className="text-sm font-semibold uppercase tracking-wide text-ink-muted">
              {t('landing.capabilitiesTitle')}
            </h2>
            <div className="mt-5 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
              {features.map(({ key, icon: Icon }) => (
                <Card key={key} className="shadow-none">
                  <CardBody>
                    <Icon aria-hidden className="size-5 text-teal-600 dark:text-teal-400" />
                    <h3 className="mt-3 text-sm font-semibold text-ink">
                      {t(`landing.features.${key}.title`)}
                    </h3>
                    <p className="mt-1.5 text-xs leading-relaxed text-ink-muted">
                      {t(`landing.features.${key}.body`)}
                    </p>
                  </CardBody>
                </Card>
              ))}
            </div>
          </div>
        </section>

        <section className="mx-auto max-w-6xl px-4 py-12">
          <h2 className="text-sm font-semibold text-ink">{t('landing.demoTitle')}</h2>
          <p className="mt-1 text-xs text-ink-muted">{t('landing.demoBody')}</p>

          <TableScroller label={t('login.demoAccounts')}>
            <table className="w-full min-w-[32rem] text-left text-sm">
              <tbody className="divide-y divide-subtle">
                {demoAccounts.map((account) => (
                  <tr key={account.email}>
                    <td className="px-4 py-2 text-xs font-medium text-ink">{account.name}</td>
                    <td className="px-4 py-2 font-mono text-xs text-ink-secondary">{account.email}</td>
                    <td className="px-4 py-2">
                      <Badge tone={account.role === 'ExternalPartner' ? 'amber' : 'teal'}>
                        {account.role}
                      </Badge>
                    </td>
                    <td className="px-4 py-2 text-xs text-ink-muted">{account.organisation}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </TableScroller>
          <p className="mt-2 font-mono text-xs text-ink-muted">
            {t('login.demoHint', { password: demoPassword })}
          </p>
        </section>
      </main>

      <footer className="border-t border-subtle bg-raised">
        <div className="mx-auto max-w-6xl px-4 py-6">
          <p className="text-xs text-ink-muted">{t('landing.footerNote')}</p>
        </div>
      </footer>
    </div>
  )
}
