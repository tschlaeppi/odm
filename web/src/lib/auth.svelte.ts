import { api, configureApi, type UserDto } from './api'
import { router } from './router.svelte'

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

export type OidcError = 'failed' | 'denied'

class AuthStore {
  session = $state<Session | null>(loadSession())

  /** Fehler der letzten Anmeldung über einen externen Anbieter (für die Anmeldeseite). */
  oidcError = $state<OidcError | null>(null)

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

  /** Nur Administratoren (serverseitig ebenfalls erzwungen). */
  get isAdmin() {
    return this.session?.user.role === 'Admin'
  }

  async login(userName: string, password: string) {
    const response = await api.login(userName, password)
    this.session = response
    // sessionStorage: nur für die Dauer des Browser-Tabs
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(response))
  }

  /**
   * Rückkehr von der Anmeldung über einen externen Anbieter: Der Server leitet mit "#/oidc?token=…" bzw.
   * "#/login?oidc=…" zurück. Das Token wird sofort aus der Adresse entfernt, damit es nicht im Verlauf bleibt.
   */
  async handleOidcReturn() {
    const hash = location.hash
    if (hash.startsWith('#/login?')) {
      const error = new URLSearchParams(hash.slice('#/login?'.length)).get('oidc')
      this.oidcError = error === 'denied' ? 'denied' : 'failed'
      router.replace('/login')
      return
    }
    if (!hash.startsWith('#/oidc?')) return

    const params = new URLSearchParams(hash.slice('#/oidc?'.length))
    const token = params.get('token')
    const expiresAt = params.get('expires')
    router.replace('/devices')
    if (!token || !expiresAt) {
      this.oidcError = 'failed'
      return
    }

    try {
      const user = await api.meWithToken(token)
      const session: Session = { token, expiresAt, user }
      this.session = session
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify(session))
      this.oidcError = null
    } catch {
      this.oidcError = 'failed'
    }
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

void auth.handleOidcReturn()
