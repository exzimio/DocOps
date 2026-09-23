import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/Button'

export function NotFoundPage() {
  const { t } = useTranslation()

  return (
    <div className="grid min-h-screen place-items-center bg-canvas px-4">
      <div className="max-w-md text-center">
        <p className="font-mono text-xs text-ink-muted">404</p>
        <h1 className="mt-2 text-lg font-semibold text-ink">{t('errors.notFoundTitle')}</h1>
        <p className="mt-1 text-sm text-ink-muted">{t('errors.notFoundBody')}</p>
        <Link to="/app/dashboard" className="mt-5 inline-block">
          <Button variant="secondary">{t('errors.backToDashboard')}</Button>
        </Link>
      </div>
    </div>
  )
}
