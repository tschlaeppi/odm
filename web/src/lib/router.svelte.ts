// Minimaler Hash-Router: kein externes Paket nötig.
function current(): string {
  return location.hash.replace(/^#/, '') || '/devices'
}

class Router {
  path = $state(current())

  constructor() {
    addEventListener('hashchange', () => (this.path = current()))
  }

  navigate(path: string) {
    location.hash = path
  }

  /** Ersetzt die aktuelle Adresse, ohne einen Verlaufseintrag anzulegen (z. B. um ein Token aus der Adresse zu entfernen). */
  replace(path: string) {
    history.replaceState(null, '', `${location.pathname}${location.search}#${path}`)
    this.path = path
  }
}

export const router = new Router()
