// Minimal hash router: no external package needed.
function current(): string {
  return location.hash.replace(/^#/, '') || '/dashboard'
}

class Router {
  path = $state(current())

  constructor() {
    addEventListener('hashchange', () => (this.path = current()))
  }

  navigate(path: string) {
    location.hash = path
  }

  /** Replaces the current address without adding a history entry (e.g. to remove a token from the address). */
  replace(path: string) {
    history.replaceState(null, '', `${location.pathname}${location.search}#${path}`)
    this.path = path
  }
}

export const router = new Router()
