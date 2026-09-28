import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { KeyRound, Pencil, UserPlus } from 'lucide-react'
import clsx from 'clsx'
import { hasPermission, useSession } from '@/features/auth/session'
import { Permission } from '@/lib/types'
import { formatDateTime } from '@/features/documents/format'
import {
  useAdminDepartments,
  useAdminRoles,
  useAdminUsers,
  useCreateUser,
  useSaveDepartment,
  useSetUserActive,
  useUpdateUser,
  type AdminUser,
  type SaveUserInput,
} from '@/features/administration/api'
import { DocumentCataloguePanel } from '@/features/administration/DocumentCataloguePanel'
import { TagsPanel } from '@/features/administration/TagsPanel'
import { RetentionPanel } from '@/features/administration/RetentionPanel'
import { SystemHealthPanel } from '@/features/administration/SystemHealthPanel'
import { Button } from '@/components/ui/Button'
import { Checkbox, TextField } from '@/components/ui/Field'
import { Badge, Card, CardBody, CardHeader, ErrorState, LoadingState } from '@/components/ui/Surface'

type Tab = 'users' | 'departments' | 'catalogue' | 'tags' | 'retention' | 'health'

const EMPTY: SaveUserInput = { firstName: '', lastName: '', roles: [], departmentIds: [] }

/**
 * Administração da organização de quem entra.
 *
 * A linha da própria conta não tem botões: `canEdit` vem a falso do servidor, que recusa na mesma
 * se alguém tentar por outra via. Não é cautela — tirar a si mesmo a administração pode deixar a
 * organização sem ninguém capaz de desfazer.
 */
export function AdminPage() {
  const { t, i18n } = useTranslation()
  const { data: me } = useSession()
  const [tab, setTab] = useState<Tab>('users')
  const [includeInactive, setIncludeInactive] = useState(false)

  const users = useAdminUsers(includeInactive)
  const roles = useAdminRoles()
  const departments = useAdminDepartments()

  const createUser = useCreateUser()
  const updateUser = useUpdateUser()
  const setActive = useSetUserActive()
  const saveDepartment = useSaveDepartment()

  const [editing, setEditing] = useState<AdminUser | null>(null)
  const [draft, setDraft] = useState<SaveUserInput & { email: string }>({ ...EMPTY, email: '' })
  const [issued, setIssued] = useState<{ email: string; password: string } | null>(null)

  const [departmentDraft, setDepartmentDraft] = useState({ id: '', name: '', code: '', description: '' })

  const canAdminUsers = hasPermission(me, Permission.adminUsers)
  const canAdminDepartments = hasPermission(me, Permission.adminDepartments)
  const canAdminTypes = hasPermission(me, Permission.adminDocumentTypes)
  // Reter documentos é uma decisão de conformidade, não de classificação: outra permissão.
  const canAdminRetention = hasPermission(me, Permission.adminSettings)

  const startCreate = () => {
    setEditing(null)
    setDraft({ ...EMPTY, email: '' })
    setIssued(null)
  }

  const startEdit = (user: AdminUser) => {
    setEditing(user)
    setIssued(null)
    setDraft({
      email: user.email,
      firstName: user.fullName.split(' ')[0] ?? '',
      lastName: user.fullName.split(' ').slice(1).join(' '),
      jobTitle: user.jobTitle ?? '',
      roles: user.roles,
      departmentIds: user.departments.map((department) => department.id),
    })
  }

  const submitUser = (event: React.FormEvent) => {
    event.preventDefault()

    const input: SaveUserInput = {
      firstName: draft.firstName.trim(),
      lastName: draft.lastName.trim(),
      jobTitle: draft.jobTitle?.trim() || undefined,
      roles: draft.roles,
      departmentIds: draft.departmentIds,
    }

    if (editing) {
      updateUser.mutate({ id: editing.id, input }, { onSuccess: () => setEditing(null) })
      return
    }

    createUser.mutate(
      { ...input, email: draft.email.trim() },
      {
        onSuccess: (created) => {
          // Mostrada uma vez e só aqui: o servidor guarda o hash e não a sabe devolver outra vez.
          setIssued({ email: created.user.email, password: created.temporaryPassword })
          setDraft({ ...EMPTY, email: '' })
        },
      },
    )
  }

  const toggle = (list: string[], value: string) =>
    list.includes(value) ? list.filter((item) => item !== value) : [...list, value]

  const error = createUser.error ?? updateUser.error ?? setActive.error ?? saveDepartment.error

  return (
    <div className="mx-auto flex max-w-[95rem] flex-col gap-4">
      <header>
        <h1 className="text-lg font-semibold text-ink">{t('admin.title')}</h1>
        <p className="text-sm text-ink-muted">{t('admin.subtitle')}</p>
      </header>

      <div className="flex gap-1 overflow-x-auto border-b border-subtle" role="tablist">
        {(
          [
            ['users', t('admin.tabs.users'), canAdminUsers],
            ['departments', t('admin.tabs.departments'), canAdminDepartments],
            ['catalogue', t('admin.tabs.catalogue'), canAdminTypes],
            ['tags', t('admin.tabs.tags'), canAdminTypes],
            ['retention', t('admin.tabs.retention'), canAdminRetention],
            ['health', t('admin.tabs.health'), canAdminRetention],
          ] as const
        )
          .filter(([, , allowed]) => allowed)
          .map(([key, label]) => (
            <button
              key={key}
              type="button"
              role="tab"
              aria-selected={tab === key}
              onClick={() => setTab(key)}
              className={clsx(
                '-mb-px shrink-0 whitespace-nowrap border-b-2 px-3 py-2 text-sm transition-colors',
                tab === key
                  ? 'border-teal-600 font-medium text-ink'
                  : 'border-transparent text-ink-muted hover:text-ink',
              )}
            >
              {label}
            </button>
          ))}
      </div>

      {error && (
        <p
          role="alert"
          className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-xs font-medium text-red-800 dark:border-red-900 dark:bg-red-950 dark:text-red-200"
        >
          {error.message}
        </p>
      )}

      {tab === 'users' && canAdminUsers && (
        <div className="grid gap-4 lg:grid-cols-[1fr_22rem]">
          <Card>
            <CardHeader
              title={t('admin.users.title')}
              actions={
                <Checkbox
                  label={t('admin.users.includeInactive')}
                  checked={includeInactive}
                  onChange={(event) => setIncludeInactive(event.target.checked)}
                />
              }
            />
            <CardBody className="p-0">
              {users.isLoading && <LoadingState label={t('common.loading')} />}
              {users.isError && <ErrorState title={t('errors.genericTitle')} body={users.error.message} />}

              {users.data && (
                <table className="w-full text-sm">
                  <thead className="border-b border-subtle text-left text-xs text-ink-muted">
                    <tr>
                      <th scope="col" className="px-4 py-2 font-medium">
                        {t('admin.users.person')}
                      </th>
                      <th scope="col" className="px-4 py-2 font-medium">
                        {t('admin.users.roles')}
                      </th>
                      <th scope="col" className="px-4 py-2 font-medium">
                        {t('admin.users.departments')}
                      </th>
                      <th scope="col" className="px-4 py-2 font-medium">
                        {t('admin.users.lastLogin')}
                      </th>
                      <th scope="col" className="px-4 py-2" />
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-subtle">
                    {users.data.map((user) => (
                      <tr key={user.id} className={user.isActive ? undefined : 'opacity-60'}>
                        <td className="px-4 py-2">
                          <span className="block text-ink">{user.fullName}</span>
                          <span className="block text-xs text-ink-muted">{user.email}</span>
                          {!user.isActive && <Badge tone="neutral">{t('admin.users.inactive')}</Badge>}
                        </td>
                        <td className="px-4 py-2 text-xs text-ink-secondary">{user.roles.join(', ')}</td>
                        <td className="px-4 py-2 text-xs text-ink-secondary">
                          {user.departments.map((department) => department.code).join(', ') || '—'}
                        </td>
                        <td className="px-4 py-2 text-xs text-ink-secondary">
                          {user.lastLoginAtUtc ? formatDateTime(user.lastLoginAtUtc, i18n.language) : '—'}
                        </td>
                        <td className="px-4 py-2">
                          {user.canEdit ? (
                            <div className="flex justify-end gap-1">
                              <Button variant="ghost" size="sm" onClick={() => startEdit(user)}>
                                <Pencil aria-hidden className="size-4" />
                                {t('common.edit')}
                              </Button>
                              <Button
                                variant="ghost"
                                size="sm"
                                loading={setActive.isPending}
                                onClick={() => setActive.mutate({ id: user.id, isActive: !user.isActive })}
                              >
                                {user.isActive ? t('admin.users.deactivate') : t('admin.users.activate')}
                              </Button>
                            </div>
                          ) : (
                            <span className="block text-right text-[11px] text-ink-muted">
                              {t(user.isPlatformAccount ? 'admin.users.platform' : 'admin.users.self')}
                            </span>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </CardBody>
          </Card>

          <Card>
            <CardHeader
              title={editing ? t('admin.users.editTitle') : t('admin.users.createTitle')}
              actions={
                editing && (
                  <Button variant="ghost" size="sm" onClick={startCreate}>
                    <UserPlus aria-hidden className="size-4" />
                    {t('admin.users.createTitle')}
                  </Button>
                )
              }
            />
            <CardBody>
              {issued && (
                <div className="mb-3 rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-950 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-100">
                  <p className="flex items-center gap-1.5 font-semibold">
                    <KeyRound aria-hidden className="size-4" />
                    {t('admin.users.passwordTitle')}
                  </p>
                  <p className="mt-1">{t('admin.users.passwordBody', { email: issued.email })}</p>
                  <p className="mt-1 break-all font-mono text-sm text-ink">{issued.password}</p>
                </div>
              )}

              <form onSubmit={submitUser} className="flex flex-col gap-3">
                {!editing && (
                  <TextField
                    label={t('common.email')}
                    type="email"
                    value={draft.email}
                    onChange={(event) => setDraft({ ...draft, email: event.target.value })}
                    required
                  />
                )}

                <TextField
                  label={t('admin.users.firstName')}
                  value={draft.firstName}
                  onChange={(event) => setDraft({ ...draft, firstName: event.target.value })}
                  required
                />
                <TextField
                  label={t('admin.users.lastName')}
                  value={draft.lastName}
                  onChange={(event) => setDraft({ ...draft, lastName: event.target.value })}
                  required
                />
                <TextField
                  label={t('admin.users.jobTitle')}
                  value={draft.jobTitle ?? ''}
                  onChange={(event) => setDraft({ ...draft, jobTitle: event.target.value })}
                />

                <fieldset className="flex flex-col gap-1.5">
                  <legend className="text-sm font-medium text-ink">{t('admin.users.roles')}</legend>
                  {roles.data?.map((role) => (
                    <Checkbox
                      key={role.name}
                      label={`${role.name} · ${t('admin.users.permissionCount', { count: role.permissionCount })}`}
                      checked={draft.roles.includes(role.name)}
                      onChange={() => setDraft({ ...draft, roles: toggle(draft.roles, role.name) })}
                    />
                  ))}
                </fieldset>

                <fieldset className="flex flex-col gap-1.5">
                  <legend className="text-sm font-medium text-ink">{t('admin.users.departments')}</legend>
                  <p className="text-[11px] text-ink-muted">{t('admin.users.primaryHint')}</p>
                  {departments.data?.map((department) => (
                    <Checkbox
                      key={department.id}
                      label={`${department.name} (${department.code})`}
                      checked={draft.departmentIds.includes(department.id)}
                      onChange={() =>
                        setDraft({ ...draft, departmentIds: toggle(draft.departmentIds, department.id) })
                      }
                    />
                  ))}
                </fieldset>

                <div className="flex justify-end gap-2">
                  {editing && (
                    <Button type="button" variant="secondary" size="sm" onClick={startCreate}>
                      {t('common.cancel')}
                    </Button>
                  )}
                  <Button
                    type="submit"
                    size="sm"
                    loading={createUser.isPending || updateUser.isPending}
                    disabled={draft.roles.length === 0}
                  >
                    {editing ? t('common.save') : t('admin.users.create')}
                  </Button>
                </div>
              </form>
            </CardBody>
          </Card>
        </div>
      )}

      {tab === 'catalogue' && canAdminTypes && <DocumentCataloguePanel />}

      {tab === 'tags' && canAdminTypes && <TagsPanel />}

      {tab === 'retention' && canAdminRetention && <RetentionPanel />}

      {tab === 'health' && canAdminRetention && <SystemHealthPanel />}

      {tab === 'departments' && canAdminDepartments && (
        <div className="grid gap-4 lg:grid-cols-[1fr_22rem]">
          <Card>
            <CardHeader title={t('admin.departments.title')} />
            <CardBody className="p-0">
              {departments.data && (
                <table className="w-full text-sm">
                  <thead className="border-b border-subtle text-left text-xs text-ink-muted">
                    <tr>
                      <th scope="col" className="px-4 py-2 font-medium">
                        {t('admin.departments.name')}
                      </th>
                      <th scope="col" className="px-4 py-2 font-medium">
                        {t('admin.departments.code')}
                      </th>
                      <th scope="col" className="px-4 py-2 font-medium">
                        {t('admin.departments.members')}
                      </th>
                      <th scope="col" className="px-4 py-2" />
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-subtle">
                    {departments.data.map((department) => (
                      <tr key={department.id}>
                        <td className="px-4 py-2">
                          <span className="block text-ink">{department.name}</span>
                          {department.description && (
                            <span className="block text-xs text-ink-muted">{department.description}</span>
                          )}
                        </td>
                        <td className="px-4 py-2 font-mono text-xs text-ink-secondary">{department.code}</td>
                        <td className="px-4 py-2 tabular-nums text-ink">{department.memberCount}</td>
                        <td className="px-4 py-2 text-right">
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() =>
                              setDepartmentDraft({
                                id: department.id,
                                name: department.name,
                                code: department.code,
                                description: department.description ?? '',
                              })
                            }
                          >
                            <Pencil aria-hidden className="size-4" />
                            {t('common.edit')}
                          </Button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </CardBody>
          </Card>

          <Card>
            <CardHeader
              title={
                departmentDraft.id ? t('admin.departments.editTitle') : t('admin.departments.createTitle')
              }
            />
            <CardBody>
              <form
                className="flex flex-col gap-3"
                onSubmit={(event) => {
                  event.preventDefault()

                  saveDepartment.mutate(
                    {
                      id: departmentDraft.id || undefined,
                      input: {
                        name: departmentDraft.name.trim(),
                        code: departmentDraft.code.trim(),
                        description: departmentDraft.description.trim() || undefined,
                      },
                    },
                    { onSuccess: () => setDepartmentDraft({ id: '', name: '', code: '', description: '' }) },
                  )
                }}
              >
                <TextField
                  label={t('admin.departments.name')}
                  value={departmentDraft.name}
                  onChange={(event) => setDepartmentDraft({ ...departmentDraft, name: event.target.value })}
                  required
                />
                <TextField
                  label={t('admin.departments.code')}
                  value={departmentDraft.code}
                  onChange={(event) => setDepartmentDraft({ ...departmentDraft, code: event.target.value })}
                  hint={t('admin.departments.codeHint')}
                  required
                />
                <TextField
                  label={t('admin.departments.description')}
                  value={departmentDraft.description}
                  onChange={(event) =>
                    setDepartmentDraft({ ...departmentDraft, description: event.target.value })
                  }
                />

                <div className="flex justify-end gap-2">
                  {departmentDraft.id && (
                    <Button
                      type="button"
                      variant="secondary"
                      size="sm"
                      onClick={() => setDepartmentDraft({ id: '', name: '', code: '', description: '' })}
                    >
                      {t('common.cancel')}
                    </Button>
                  )}
                  <Button type="submit" size="sm" loading={saveDepartment.isPending}>
                    {t('common.save')}
                  </Button>
                </div>
              </form>
            </CardBody>
          </Card>
        </div>
      )}
    </div>
  )
}
