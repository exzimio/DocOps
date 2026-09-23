import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ExternalLink, Play, Settings } from 'lucide-react'
import { useRunIntegration, useUpdateIntegration } from './api'
import { IntegrationLogDialog } from './IntegrationLogDialog'
import { WebhookSettingsDialog } from './WebhookSettingsDialog'
import { StatusBadge, SimulationNotice } from './presentation'
import { integrationIcon } from './labels'
import { webhookEndpoint } from './settings'
import { IntegrationKind, type IntegrationCard } from './types'
import { formatDateTime } from '@/features/documents/format'
import { Button } from '@/components/ui/Button'
import { Card, CardBody, CardHeader } from '@/components/ui/Surface'
import { Checkbox } from '@/components/ui/Field'

interface IntegrationCardViewProps {
  integration: IntegrationCard
  canManage: boolean
}

/**
 * One integration, on its own card. Every number on it — records processed, errors, last activity —
 * is read straight from the row the server keeps, which the server only ever updates from a run that
 * actually happened. There is nothing here for the card to invent.
 */
export function IntegrationCardView({ integration, canManage }: IntegrationCardViewProps) {
  const { t, i18n } = useTranslation()
  const runIntegration = useRunIntegration()
  const updateIntegration = useUpdateIntegration()

  const [showLog, setShowLog] = useState(false)
  const [showWebhookSettings, setShowWebhookSettings] = useState(false)

  const Icon = integrationIcon(integration.kind)

  const runError = runIntegration.error
    ? t([`integrations.errors.${runIntegration.error.code}`, 'errors.genericBody'] as const)
    : null

  const toggleEnabled = () => {
    updateIntegration.mutate({ integrationId: integration.id, isEnabled: !integration.isEnabled })
  }

  return (
    // Named as a region: a grid of cards otherwise gives a screen reader no way to tell them apart
    // or to jump straight to the one it wants.
    <Card role="region" aria-label={integration.name}>
      <CardHeader
        title={integration.name}
        description={integration.description ?? undefined}
        // The icon is picked from a fixed lookup keyed by the integration's kind, which never
        // changes for a given card — the same shape DesignerNode.tsx uses for a node's icon.
        // oxlint-disable-next-line react/static-components
        icon={<Icon className="size-4" />}
        actions={<StatusBadge status={integration.status} />}
      />
      <CardBody className="flex flex-col gap-3">
        {integration.isSimulated && integration.simulationNotice && (
          <SimulationNotice notice={integration.simulationNotice} />
        )}

        {!integration.hasAdapter && (
          <p className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-800 dark:border-red-900 dark:bg-red-950 dark:text-red-200">
            {t('integrations.noAdapter')}
          </p>
        )}

        <dl className="grid grid-cols-2 gap-x-4 gap-y-1.5 text-xs">
          <div>
            <dt className="text-ink-muted">{t('integrations.lastRun')}</dt>
            <dd className="text-ink">
              {integration.lastRunAtUtc ? formatDateTime(integration.lastRunAtUtc, i18n.language) : '—'}
            </dd>
          </div>
          <div>
            <dt className="text-ink-muted">{t('integrations.lastActivity')}</dt>
            <dd className="text-ink">
              {integration.lastActivityAtUtc
                ? formatDateTime(integration.lastActivityAtUtc, i18n.language)
                : '—'}
            </dd>
          </div>
          <div>
            <dt className="text-ink-muted">{t('integrations.recordsProcessed')}</dt>
            <dd className="text-ink tabular-nums">{integration.recordsProcessed}</dd>
          </div>
          <div>
            <dt className="text-ink-muted">{t('integrations.errorCount')}</dt>
            <dd className={integration.errorCount > 0 ? 'text-red-700 dark:text-red-300' : 'text-ink'}>
              {integration.errorCount}
            </dd>
          </div>
        </dl>

        {integration.lastRunSummary && (
          <p className="break-words text-xs text-ink-secondary">{integration.lastRunSummary}</p>
        )}

        {runError && (
          <p role="alert" className="text-xs font-medium text-red-600 dark:text-red-400">
            {runError}
          </p>
        )}

        <div className="flex flex-wrap items-center justify-between gap-2 border-t border-subtle pt-3">
          <div className="flex flex-wrap gap-2">
            {canManage && integration.hasAdapter && (
              <Button
                variant="secondary"
                size="sm"
                onClick={() => runIntegration.mutate(integration.id)}
                loading={runIntegration.isPending}
                disabled={!integration.isEnabled}
              >
                <Play aria-hidden className="size-4" />
                {t('integrations.runNow')}
              </Button>
            )}

            <Button variant="ghost" size="sm" onClick={() => setShowLog(true)}>
              <ExternalLink aria-hidden className="size-4" />
              {t('integrations.viewLog')}
            </Button>

            {canManage && integration.kind === IntegrationKind.Webhooks && (
              <Button variant="ghost" size="sm" onClick={() => setShowWebhookSettings(true)}>
                <Settings aria-hidden className="size-4" />
                {t('integrations.configure')}
              </Button>
            )}
          </div>

          {canManage && (
            <Checkbox
              label={t('integrations.enabled')}
              checked={integration.isEnabled}
              onChange={toggleEnabled}
            />
          )}
        </div>
      </CardBody>

      {showLog && <IntegrationLogDialog integration={integration} onClose={() => setShowLog(false)} />}

      {showWebhookSettings && (
        <WebhookSettingsDialog
          integration={integration}
          currentEndpoint={webhookEndpoint(integration.settingsJson)}
          onClose={() => setShowWebhookSettings(false)}
          onSaved={() => setShowWebhookSettings(false)}
        />
      )}
    </Card>
  )
}
