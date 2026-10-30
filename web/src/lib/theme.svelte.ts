export type Theme = 'light' | 'dark'

const STORAGE_KEY = 'odm.theme'

function initialTheme(): Theme {
  const stored = localStorage.getItem(STORAGE_KEY)
  if (stored === 'light' || stored === 'dark') return stored
  return matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

class ThemeStore {
  current = $state<Theme>(initialTheme())

  constructor() {
    this.apply()
  }

  toggle() {
    this.current = this.current === 'dark' ? 'light' : 'dark'
    localStorage.setItem(STORAGE_KEY, this.current)
    this.apply()
  }

  private apply() {
    document.documentElement.dataset.theme = this.current
  }
}

export const theme = new ThemeStore()
