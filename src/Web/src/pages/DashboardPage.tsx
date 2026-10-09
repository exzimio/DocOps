import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Building2, IdCard, KeyRound, Users } from 'lucide-react'
import { api, type ApiError } from '@/lib/api'
import type { TenantProfile } from '@/lib/types'
import { UserType } from '@/lib/types'
import { useSession } from '@/features/auth/session'
import {
  Badge,
  Card,
  CardBody,
  CardHeader,
  DataRow,
  ErrorState,
  LoadingState,
  TableScroller,
} from '@/components/ui/Surface'

function formatDateTime(value: string | null, locale: string): string | null {
  if (!value) {
    return null
  }

  return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
}

export function DashboardPage() {
  const { t, i18n } = useTranslation()
  const { data: user } = useSession()

  const tenantQuery = useQuery<TenantProfile, ApiError>({
    queryKey: ['tenant', 'current'],
    queryFn: () => api.get<TenantProfile>('/api/tenants/current'),
  })

  if (!user) {
    return <LoadingState label={t('common.loading')} />
  }

  const lastLogin = formatDateTime(user.lastLoginAtUtc, i18n.language)

  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-5">
      <header>
        <h1 className="text-lg font-semibold text-ink">{t('dashboard.greeting', { name: user.fullName })}</h1>
        <p className="text-sm text-ink-muted">{t('dashboard.subtitle', { tenant: user.tenantName })}</p>
      </header>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader title={t('dashboard.identityTitle')} icon={<IdCard className="size-4" />} />
          <CardBody>
            <dl className="divide-y divide-subtle">
              <DataRow label={t('common.email')}>
                <span className="font-mono text-xs">{user.email}</span>
              </DataRow>
              <DataRow label={t('dashboard.jobTitle')}>{user.jobTitle ?? '-'}</DataRow>
              <DataRow label={t('dashboard.roles')}>
                <span className="flex flex-wrap justify-end gap-1">
                  {user.roles.map((role) => (
                    <Badge key={role} tone="teal">
                      {role}
                    </Badge>
                  ))}
                </span>
              </DataRow>
              <DataRow label={t('dashboard.userType')}>
                {user.userType === UserType.External
                  ? t('dashboard.userTypeExternal')
                  : t('dashboard.userTypeInternal')}
              </DataRow>
              <DataRow label={t('dashboard.primaryDepartment')}>
                {user.departments.find((department) => department.isPrimary)?.name ?? '-'}
              </DataRow>
              <DataRow label={t('dashboard.lastLogin')}>{lastLogin ?? t('dashboard.never')}</DataRow>
              {user.isPlatformAdministrator && (
                <DataRow label={t('dashboard.platformAdmin')}>
                  <Badge tone="amber">✓</Badge>
                </DataRow>
              )}
            </dl>
          </CardBody>
        </Card>

        <Card>
          <CardHeader title={t('dashboard.organisationTitle')} icon={<Building2 className="size-4" />} />
          <CardBody>
            {tenantQuery.isLoading && <LoadingState label={t('common.loading')} />}
            {tenantQuery.isError && (
              <ErrorState
                title={t('errors.genericTitle')}
                body={tenantQuery.error.message}
                correlationId={tenantQuery.error.problem.correlationId}
              />
            )}
            {tenantQuery.data && (
              <dl className="divide-y divide-subtle">
                <DataRow label={t('dashboard.organisationTitle')}>{tenantQuery.data.name}</DataRow>
                <DataRow label={t('dashboard.legalName')}>{tenantQuery.data.legalName ?? '-'}</DataRow>
                <DataRow label={t('dashboard.timeZone')}>{tenantQuery.data.timeZoneId}</DataRow>
                <DataRow label={t('common.language')}>{tenantQuery.data.defaultCulture}</DataRow>
              </dl>
            )}
          </CardBody>
        </Card>
      </div>

      <Card>
        <CardHeader
          title={t('dashboard.departmentsTitle')}
          icon={<Users className="size-4" />}
          actions={tenantQuery.data ? <Badge>{tenantQuery.data.departments.length}</Badge> : undefined}
        />
        <CardBody className="p-0">
          {tenantQuery.data && (
            <TableScroller label={t('dashboard.recent')}>
              <table className="w-full min-w-[36rem] text-left text-sm">
                <thead className="bg-sunken text-[11px] uppercase tracking-wide text-ink-muted">
                  <tr>
                    <th scope="col" className="px-4 py-2 font-medium">
                      {t('dashboard.departmentsTitle')}
                    </th>
                    <th scope="col" className="px-4 py-2 font-medium">
                      {t('dashboard.manager')}
                    </th>
                    <th scope="col" className="px-4 py-2 text-right font-medium">
                      {t('dashboard.memberCountLabel')}
                    </th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-subtle">
                  {tenantQuery.data.departments.map((department) => (
                    <tr key={department.id} className="hover:bg-sunken">
                      <td className="px-4 py-2">
                        <div className="flex items-center gap-2">
                          <Badge>{department.code}</Badge>
                          <span className="font-medium text-ink">{department.name}</span>
                        </div>
                        {department.description && (
                          <p className="mt-0.5 text-[11px] text-ink-muted">{department.description}</p>
                        )}
                      </td>
                      <td className="px-4 py-2 text-xs text-ink-secondary">
                        {department.managerDisplayName ?? (
                          <span className="text-ink-muted">{t('dashboard.noManager')}</span>
                        )}
                      </td>
                      <td className="px-4 py-2 text-right tabular-nums text-ink-secondary">
                        {department.memberCount}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </TableScroller>
          )}
        </CardBody>
      </Card>

      <Card>
        <CardHeader
          title={t('dashboard.permissionsTitle')}
          description={t('dashboard.permissionsBody')}
          icon={<KeyRound className="size-4" />}
          actions={<Badge tone="teal">{user.permissions.length}</Badge>}
        />
        <CardBody>
          <ul className="flex flex-wrap gap-1.5">
            {user.permissions.map((permission) => (
              <li key={permission}>
                <code className="rounded-xs border border-subtle bg-sunken px-1.5 py-0.5 font-mono text-[11px] text-ink-secondary">
                  {permission}
                </code>
              </li>
            ))}
          </ul>
        </CardBody>
      </Card>
    </div>
  )
}
