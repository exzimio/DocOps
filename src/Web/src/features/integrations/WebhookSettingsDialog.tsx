import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { X } from 'lucide-react'
import { useUpdateIntegration } from './api'
import type { IntegrationCard } from './types'
import { Button } from '@/components/ui/Button'
import { TextField } from '@/components/ui/Field'

interface WebhookSettingsDialogProps {
  integration: IntegrationCard
  currentEndpoint: string
  onClose: () => void
  onSaved: () => void
}

/**
 * Sets where webhook events are delivered.
 *
 * The address is checked here for the message, but the check that counts runs on the server: an
 * administrator typing an address this server would then call is exactly the shape of server-side
 * request forgery, so the server refuses a loopback or private address regardless of what this
 * dialog thinks of it first.
 */
export function WebhookSettingsDialog({
  integration,
  currentEndpoint,
  onClose,
  onSaved,
}: WebhookSettingsDialogProps) {
  const { t } = useTranslation()
  const [endpoint, setEndpoint] = useState(currentEndpoint)
  const updateIntegration = useUpdateIntegration()

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }

    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [onClose])

  const submit = async (event: React.FormEvent) => {
    event.preventDefault()

    try {
      await updateIntegration.mutateAsync({
        integrationId: integration.id,
        isEnabled: integration.isEnabled,
        settingsJson: JSON.stringify({ endpoint: endpoint.trim() || null }),
      })
      onSaved()
    } catch {
      // Surfaced from updateIntegration.error below.
    }
  }

  const serverError = updateIntegration.error
    ? t([`integrations.errors.${updateIntegration.error.code}`, 'errors.genericBody'] as const)
    : null

  // O servidor sabe qual das regras recusou o endereço — HTTPS, endereço da própria máquina, gama
  // privada — e essa razão é o que torna o erro acionável. A mensagem traduzida diz o que falhou; o
  // detalhe do servidor diz porquê, e sem ele quem escreveu o endereço fica a adivinhar.
  const serverReason = updateIntegration.error?.problem.detail ?? null

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-graphite-950/50 p-4"
      role="dialog"
      aria-modal="true"
      aria-label={t('integrations.webhookSettingsTitle')}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose()
      }}
    >
      <div className="mt-8 w-full max-w-lg rounded-lg border border-subtle bg-raised shadow-overlay">
        <div className="flex items-center justify-between border-b border-subtle px-4 py-3">
          <h2 className="text-sm font-semibold text-ink">{t('integrations.webhookSettingsTitle')}</h2>
          <Button variant="ghost" size="sm" onClick={onClose} aria-label={t('common.close')}>
            <X aria-hidden className="size-4" />
          </Button>
        </div>

        <form onSubmit={submit} className="flex flex-col gap-4 p-4">
          <TextField
            label={t('integrations.webhookEndpoint')}
            type="url"
            value={endpoint}
            onChange={(event) => setEndpoint(event.target.value)}
            placeholder="https://exemplo.com/webhooks/docops"
            hint={t('integrations.webhookEndpointHint')}
          />

          {serverError && (
            <div
              role="alert"
              className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-red-800 dark:border-red-900 dark:bg-red-950 dark:text-red-200"
            >
              <p className="text-xs font-medium">{serverError}</p>
              {serverReason && serverReason !== serverError && (
                <p className="mt-0.5 text-xs">{serverReason}</p>
              )}
            </div>
          )}

          <div className="flex justify-end gap-2">
            <Button type="button" variant="secondary" onClick={onClose}>
              {t('common.cancel')}
            </Button>
            <Button type="submit" loading={updateIntegration.isPending}>
              {t('integrations.save')}
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}
