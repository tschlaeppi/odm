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

  /** Error of the last sign-in through an external provider (for the sign-in page). */
  oidcError = $state<OidcError | null>(null)

  get isAuthenticated() {
    return this.session !== null
  }

  get user() {
    return this.session?.user ?? null
  }

  /** Operators and administrators may change things (also enforced server-side). */
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
    // sessionStorage: only for the lifetime of the browser tab
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(response))
  }

  /**
   * Return from sign-in through an external provider: the server redirects with "#/oidc?token=…" or
   * "#/login?oidc=…". The token is removed from the address immediately so that it does not stay in the history.
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
