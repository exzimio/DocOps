import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import ptPT from './locales/pt-PT.json'
import en from './locales/en.json'

export const supportedLanguages = ['pt-PT', 'en'] as const
export type SupportedLanguage = (typeof supportedLanguages)[number]

const LANGUAGE_STORAGE_KEY = 'docops.language'

function resolveInitialLanguage(): SupportedLanguage {
  const stored = localStorage.getItem(LANGUAGE_STORAGE_KEY)
  if (stored && supportedLanguages.includes(stored as SupportedLanguage)) {
    return stored as SupportedLanguage
  }

  return navigator.language.startsWith('en') ? 'en' : 'pt-PT'
}

void i18n.use(initReactI18next).init({
  resources: {
    'pt-PT': { translation: ptPT },
    en: { translation: en },
  },
  lng: resolveInitialLanguage(),
  fallbackLng: 'pt-PT',
  supportedLngs: [...supportedLanguages],
  interpolation: { escapeValue: false },
})

export function changeLanguage(language: SupportedLanguage): void {
  localStorage.setItem(LANGUAGE_STORAGE_KEY, language)
  void i18n.changeLanguage(language)
  document.documentElement.lang = language
}

document.documentElement.lang = i18n.language

export default i18n
