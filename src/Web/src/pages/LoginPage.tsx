import { useEffect } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { ArrowLeft, ShieldCheck } from 'lucide-react'
import { useLogin, useSession } from '@/features/auth/session'
import { demoAccounts, demoPassword } from '@/features/auth/demoAccounts'
import { Button } from '@/components/ui/Button'
import { Checkbox, TextField } from '@/components/ui/Field'
import { Badge, Card, CardBody, CardHeader, LoadingState } from '@/components/ui/Surface'
import { LanguageSwitcher, ThemeToggle } from '@/components/PreferencesControls'

const schema = z.object({
  email: z.string().min(1, 'login.validation.emailRequired').email('login.validation.emailInvalid'),
  password: z.string().min(1, 'login.validation.passwordRequired'),
  rememberMe: z.boolean(),
})

type LoginFormValues = z.infer<typeof schema>

export function LoginPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const location = useLocation()
  const { data: user, isLoading } = useSession()
  const login = useLogin()

  const redirectTo = (location.state as { from?: string } | null)?.from ?? '/app/dashboard'

  const {
    register,
    handleSubmit,
    setValue,
    setFocus,
    formState: { errors, isSubmitting },
  } = useForm<LoginFormValues>({
    resolver: zodResolver(schema),
    defaultValues: { email: '', password: '', rememberMe: false },
  })

  useEffect(() => setFocus('email'), [setFocus])

  if (isLoading) {
    return <LoadingState label={t('common.loading')} />
  }

  if (user) {
    return <Navigate to={redirectTo} replace />
  }

  const onSubmit = handleSubmit(async (values) => {
    try {
      await login.mutateAsync(values)
      navigate(redirectTo, { replace: true })
    } catch {
      // Rendered from `login.error` below; the rejection itself needs no further handling.
    }
  })

  const serverErrorKey = login.error ? `login.errors.${login.error.code}` : null
  const serverErrorMessage = serverErrorKey ? t([serverErrorKey, 'errors.genericBody'] as const) : null

  const fillAccount = (email: string) => {
    setValue('email', email, { shouldValidate: true })
    setValue('password', demoPassword, { shouldValidate: true })
    setFocus('password')
  }

  return (
    <div className="grid min-h-dvh lg:grid-cols-[1fr_minmax(0,28rem)]">
      <section className="hidden flex-col justify-between bg-graphite-900 p-10 text-graphite-100 lg:flex">
        <Link to="/" className="flex items-center gap-2 text-sm text-graphite-300 hover:text-white">
          <ArrowLeft aria-hidden className="size-4" />
          {t('common.appName')}
        </Link>

        <div className="max-w-md">
          <h1 className="text-balance text-3xl font-semibold text-white">{t('login.title')}</h1>
          <p className="mt-3 text-sm text-graphite-300">{t('landing.heroBody')}</p>
        </div>

        <p className="text-xs text-graphite-400">{t('common.independentProject')}</p>
      </section>

      <section className="flex flex-col gap-6 p-6 sm:p-10">
        <div className="flex items-center justify-between">
          <span className="grid size-8 place-items-center rounded-md bg-teal-700 text-white">
            <ShieldCheck aria-hidden className="size-4" />
          </span>
          <div className="flex items-center gap-2">
            <LanguageSwitcher />
            <ThemeToggle />
          </div>
        </div>

        <div>
          <h2 className="text-xl font-semibold text-ink">{t('login.title')}</h2>
          <p className="mt-1 text-sm text-ink-muted">{t('login.subtitle')}</p>
        </div>

        <form onSubmit={onSubmit} noValidate className="flex flex-col gap-4">
          <TextField
            label={t('common.email')}
            type="email"
            autoComplete="username"
            error={errors.email ? t(errors.email.message ?? '') : undefined}
            {...register('email')}
          />

          <TextField
            label={t('common.password')}
            type="password"
            autoComplete="current-password"
            error={errors.password ? t(errors.password.message ?? '') : undefined}
            {...register('password')}
          />

          <Checkbox label={t('common.rememberMe')} {...register('rememberMe')} />

          {serverErrorMessage && (
            <p
              role="alert"
              className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-xs font-medium text-red-800 dark:border-red-900 dark:bg-red-950 dark:text-red-200"
            >
              {serverErrorMessage}
            </p>
          )}

          <Button type="submit" size="lg" loading={isSubmitting || login.isPending}>
            {isSubmitting || login.isPending ? t('login.submitting') : t('login.submit')}
          </Button>
        </form>

        <Card>
          <CardHeader
            title={t('login.demoAccounts')}
            description={t('login.demoHint', { password: demoPassword })}
          />
          <CardBody className="p-0">
            <ul className="divide-y divide-subtle">
              {demoAccounts.map((account) => (
                <li key={account.email} className="flex items-center justify-between gap-3 px-4 py-2">
                  <div className="min-w-0">
                    <p className="truncate text-xs font-medium text-ink">{account.name}</p>
                    <p className="truncate text-[11px] text-ink-muted">{account.email}</p>
                  </div>
                  <div className="flex shrink-0 items-center gap-2">
                    <Badge tone={account.role === 'ExternalPartner' ? 'amber' : 'teal'}>{account.role}</Badge>
                    <Button variant="secondary" size="sm" onClick={() => fillAccount(account.email)}>
                      {t('login.useAccount')}
                    </Button>
                  </div>
                </li>
              ))}
            </ul>
          </CardBody>
        </Card>
      </section>
    </div>
  )
}
