import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useSession } from './session'
import { ErrorState, LoadingState } from '@/components/ui/Surface'
import { Button } from '@/components/ui/Button'

/**
 * Client-side gate for authenticated routes. It is a navigation convenience only — the API rejects
 * every unauthenticated or unauthorised request on its own.
 */
export function RequireAuth() {
  const { t } = useTranslation()
  const location = useLocation()
  const { data: user, isLoading, isError, error, refetch } = useSession()

  if (isLoading) {
    return <LoadingState label={t('common.loading')} />
  }

  if (isError) {
    return (
      <ErrorState
        title={t('errors.genericTitle')}
        body={error?.message ?? t('errors.genericBody')}
        correlationId={error?.problem.correlationId}
        action={
          <Button variant="secondary" size="sm" onClick={() => void refetch()}>
            {t('common.retry')}
          </Button>
        }
      />
    )
  }

  if (!user) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />
  }

  return <Outlet />
}
