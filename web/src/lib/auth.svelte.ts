import { api, configureApi, type UserDto } from './api'

const STORAGE_KEY = 'odm.session'

interface Session {
  token: string
  expiresAt: string
  user: UserDto
}

function loadSession(): Session | null {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY)
    if (!raw) return null
    const session = JSON.parse(raw) as Session
    return new Date(session.expiresAt) > new Date() ? session : null
  } catch {
    return null
  }
}

class AuthStore {
  session = $state<Session | null>(loadSession())

  get isAuthenticated() {
    return this.session !== null
  }

  get user() {
    return this.session?.user ?? null
  }

  /** Operatoren und Administratoren dürfen ändern (serverseitig ebenfalls erzwungen). */
  get canEdit() {
    const role = this.session?.user.role
    return role === 'Admin' || role === 'Operator'
  }

  async login(userName: string, password: string) {
    const response = await api.login(userName, password)
    this.session = response
    // sessionStorage: nur für die Dauer des Browser-Tabs
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(response))
  }

  logout() {
    this.session = null
    sessionStorage.removeItem(STORAGE_KEY)
  }
}

export const auth = new AuthStore()

configureApi({
  token: () => auth.session?.token ?? null,
  onUnauthorized: () => auth.logout(),
})
