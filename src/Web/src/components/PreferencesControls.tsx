import { useTranslation } from 'react-i18next'
import { Languages, Moon, Sun } from 'lucide-react'
import { changeLanguage, supportedLanguages, type SupportedLanguage } from '@/i18n'
import { useTheme } from '@/lib/theme'
import { Button } from '@/components/ui/Button'

export function ThemeToggle() {
  const { t } = useTranslation()
  const { theme, toggle } = useTheme()
  const nextLabel = theme === 'dark' ? t('common.themeLight') : t('common.themeDark')

  return (
    <Button
      variant="ghost"
      size="sm"
      onClick={toggle}
      title={`${t('common.theme')}: ${nextLabel}`}
      aria-label={`${t('common.theme')}: ${nextLabel}`}
    >
      {theme === 'dark' ? <Sun aria-hidden className="size-4" /> : <Moon aria-hidden className="size-4" />}
    </Button>
  )
}

export function LanguageSwitcher() {
  const { t, i18n } = useTranslation()

  return (
    <label className="flex items-center gap-1.5 text-xs text-ink-secondary">
      <Languages aria-hidden className="size-4" />
      <span className="sr-only">{t('common.language')}</span>
      <select
        value={supportedLanguages.includes(i18n.language as SupportedLanguage) ? i18n.language : 'pt-PT'}
        onChange={(event) => changeLanguage(event.target.value as SupportedLanguage)}
        className="h-8 rounded-md border border-strong bg-raised px-2 text-xs text-ink"
      >
        <option value="pt-PT">Português (PT)</option>
        <option value="en">English</option>
      </select>
    </label>
  )
}
