import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { Bell } from 'lucide-react'
import clsx from 'clsx'
import { useMarkNotificationsRead, useNotifications, useUnreadNotificationCount } from './api'
import { formatDateTime } from '@/features/documents/format'
import { Button } from '@/components/ui/Button'

export function NotificationBell() {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)
  const containerRef = useRef<HTMLDivElement>(null)

  const unreadCount = useUnreadNotificationCount()
  const notifications = useNotifications()
  const markRead = useMarkNotificationsRead()

  useEffect(() => {
    if (!open) {
      return
    }

    const onPointerDown = (event: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setOpen(false)
      }
    }

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setOpen(false)
    }

    document.addEventListener('mousedown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)

    return () => {
      document.removeEventListener('mousedown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open])

  const count = unreadCount.data ?? 0

  return (
    <div ref={containerRef} className="relative">
      <Button
        variant="ghost"
        size="sm"
        onClick={() => setOpen((current) => !current)}
        aria-label={t('notifications.open')}
        aria-expanded={open}
        title={t('notifications.title')}
      >
        <span className="relative">
          <Bell aria-hidden className="size-4" />
          {count > 0 && (
            <span className="absolute -right-1.5 -top-1.5 grid min-w-4 place-items-center rounded-full bg-amber-500 px-1 text-[10px] font-semibold text-graphite-950">
              {count > 9 ? '9+' : count}
            </span>
          )}
        </span>
      </Button>

      {open && (
        <div className="absolute right-0 z-40 mt-2 w-80 rounded-lg border border-subtle bg-raised shadow-overlay">
          <div className="flex items-center justify-between border-b border-subtle px-3 py-2">
            <p className="text-xs font-semibold text-ink">{t('notifications.title')}</p>
            {count > 0 && (
              <button
                type="button"
                onClick={() => markRead.mutate('all')}
                className="text-[11px] text-teal-700 hover:underline dark:text-teal-300"
              >
                {t('notifications.markAllRead')}
              </button>
            )}
          </div>

          <ul className="max-h-96 divide-y divide-subtle overflow-y-auto">
            {notifications.data?.length === 0 && (
              <li className="px-3 py-6 text-center text-xs text-ink-muted">{t('notifications.empty')}</li>
            )}

            {notifications.data?.map((notification) => (
              <li key={notification.id}>
                <button
                  type="button"
                  onClick={() => {
                    if (!notification.isRead) {
                      markRead.mutate(notification.id)
                    }

                    if (notification.link) {
                      navigate(notification.link)
                    }

                    setOpen(false)
                  }}
                  className={clsx(
                    'flex w-full flex-col items-start gap-0.5 px-3 py-2 text-left transition-colors hover:bg-sunken',
                    !notification.isRead && 'bg-teal-50/60 dark:bg-teal-950/40',
                  )}
                >
                  <span className="text-xs font-medium text-ink">{notification.title}</span>
                  {notification.body && (
                    <span className="text-[11px] text-ink-muted">{notification.body}</span>
                  )}
                  <span className="text-[11px] text-ink-muted">
                    {formatDateTime(notification.createdAtUtc, i18n.language)}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  )
}
