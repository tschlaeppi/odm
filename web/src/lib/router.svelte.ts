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
}

export const router = new Router()
