import type { PackageKind, PackageSpec } from './api'

/** Form state: empty fields as '' (inputs), back to null for the server. */
export interface SpecForm {
  identifier: string
  version: string
  scope: string
  source: string
  remote: string
  channel: string
  classic: boolean
  arguments: string
}

export const repoKinds: PackageKind[] = ['Winget', 'Chocolatey', 'Apt', 'Rpm', 'Flatpak', 'Snap']
export const allKinds: PackageKind[] = ['Psadt', ...repoKinds]

export function emptySpec(kind: PackageKind): SpecForm {
  return {
    identifier: '',
    version: '',
    scope: kind === 'Winget' ? 'machine' : kind === 'Flatpak' ? 'system' : '',
    source: '',
    remote: kind === 'Flatpak' ? 'flathub' : '',
    channel: kind === 'Snap' ? 'stable' : '',
    classic: false,
    arguments: '',
  }
}

export function specFromDto(spec: PackageSpec): SpecForm {
  return {
    identifier: spec.identifier ?? '',
    version: spec.version ?? '',
    scope: spec.scope ?? '',
    source: spec.source ?? '',
    remote: spec.remote ?? '',
    channel: spec.channel ?? '',
    classic: spec.classic,
    arguments: spec.arguments ?? '',
  }
}

export function specToDto(form: SpecForm): PackageSpec {
  const orNull = (value: string) => (value.trim() === '' ? null : value.trim())
  return {
    identifier: orNull(form.identifier),
    version: orNull(form.version),
    scope: orNull(form.scope),
    source: orNull(form.source),
    remote: orNull(form.remote),
    channel: orNull(form.channel),
    classic: form.classic,
    arguments: orNull(form.arguments),
  }
}
