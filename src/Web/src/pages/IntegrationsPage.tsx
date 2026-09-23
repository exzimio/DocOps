import { useTranslation } from 'react-i18next'
import { hasPermission, useSession } from '@/features/auth/session'
import { Permission } from '@/lib/types'
import { useIntegrations } from '@/features/integrations/api'
import { IntegrationCardView } from '@/features/integrations/IntegrationCardView'
import { Button } from '@/components/ui/Button'
import { ErrorState, LoadingState } from '@/components/ui/Surface'

/**
 * The integrations centre: one card per connected system, each backed by real code. A card only
 * exists because an adapter exists for it (see `hasAdapter` on every card returned); nothing here is
 * a promise of a screen still to come.
 */
export function IntegrationsPage() {
  const { t } = useTranslation()
  const { data: user } = useSession()

  const integrationsQuery = useIntegrations()
  const canManage = hasPermission(user, Permission.integrationsManage)

  return (
    <div className="mx-auto flex max-w-[95rem] flex-col gap-4">
      <header>
        <h1 className="text-lg font-semibold text-ink">{t('integrations.title')}</h1>
        <p className="text-sm text-ink-muted">{t('integrations.subtitle')}</p>
      </header>

      {integrationsQuery.isLoading && <LoadingState label={t('common.loading')} />}

      {integrationsQuery.isError && (
        <ErrorState
          title={t('errors.genericTitle')}
          body={integrationsQuery.error.message}
          correlationId={integrationsQuery.error.problem.correlationId}
          action={
            <Button variant="secondary" size="sm" onClick={() => void integrationsQuery.refetch()}>
              {t('common.retry')}
            </Button>
          }
        />
      )}

      {integrationsQuery.data && (
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {integrationsQuery.data.map((integration) => (
            <IntegrationCardView key={integration.id} integration={integration} canManage={canManage} />
          ))}
        </div>
      )}
    </div>
  )
}
