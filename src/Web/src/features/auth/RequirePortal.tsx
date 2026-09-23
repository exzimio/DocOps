import { Navigate, Outlet } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { hasPermission, useSession } from './session'
import { Permission } from '@/lib/types'
import { LoadingState } from '@/components/ui/Surface'

/**
 * Keeps the two experiences apart.
 *
 * An internal user who types `/portal` is sent to the application, and an external user who types
 * `/app` is sent to the portal. This is navigation, not security: the API refuses either way round —
 * no internal role holds `portal.access`, and no external one holds anything the application needs.
 * What it buys is that neither side ever lands on a screen that is not meant for them and has to
 * work out why it is empty.
 */
export function RequirePortal({ external }: { external: boolean }) {
  const { t } = useTranslation()
  const { data: user, isLoading } = useSession()

  if (isLoading) {
    return <LoadingState label={t('common.loading')} />
  }

  if (!user) {
    return <Navigate to="/login" replace />
  }

  const belongsInPortal = hasPermission(user, Permission.portalAccess)

  if (external && !belongsInPortal) {
    return <Navigate to="/app/dashboard" replace />
  }

  if (!external && belongsInPortal) {
    return <Navigate to="/portal" replace />
  }

  return <Outlet />
}
