export interface UserDto {
  id: string
  userName: string
  displayName: string
  role: string
}

export interface OidcProviderDto {
  id: string
  displayName: string
}

export interface LoginResponse {
  token: string
  expiresAt: string
  user: UserDto
}

export interface DeviceDto {
  id: string
  name: string
  osFamily: 'Windows' | 'Linux'
  osName: string
  osVersion: string
  primaryUser: string | null
  orgUnit: string | null
  costCenter: string | null
  registeredAt: string
  lastSeenAt: string | null
  agentVersion: string | null
}

export interface AssignmentInput {
  primaryUser: string | null
  orgUnit: string | null
  costCenter: string | null
}

export type GroupField = 'Name' | 'OsFamily' | 'OsName' | 'OsVersion' | 'PrimaryUser' | 'OrgUnit' | 'CostCenter'
export type MatchOperator = 'Is' | 'StartsWith' | 'EndsWith' | 'Contains'
export type MatchMode = 'All' | 'Any'

export interface GroupRule {
  field: GroupField
  operator: MatchOperator
  value: string
}

export interface GroupInput {
  name: string
  description: string | null
  matchMode: MatchMode
  rules: GroupRule[]
}

export interface GroupDto extends GroupInput {
  id: string
  deviceCount: number
}

export interface TokenDto {
  id: string
  label: string
  createdAt: string
  expiresAt: string
  usedAt: string | null
  status: 'Active' | 'Used' | 'Expired'
}

export interface TokenCreatedDto {
  id: string
  label: string
  token: string
  expiresAt: string
}

export interface JobDto {
  id: string
  deviceId: string
  type: string
  status: 'Pending' | 'Delivered' | 'Succeeded' | 'Failed'
  createdAt: string
  completedAt: string | null
  output: string | null
}

export type PackageKind = 'Psadt' | 'Winget' | 'Chocolatey' | 'Apt' | 'Rpm' | 'Flatpak' | 'Snap'

/** Paketeinstellungen; welche Felder gelten, hängt von der Art ab. */
export interface PackageSpec {
  identifier: string | null
  version: string | null
  scope: string | null
  source: string | null
  remote: string | null
  channel: string | null
  classic: boolean
  arguments: string | null
}

export interface SecretDto {
  name: string
  updatedAt: string
}

export interface PackageDto {
  id: string
  name: string
  description: string | null
  kind: PackageKind
  createdAt: string
  latestVersion: string | null
  versionCount: number
  spec: PackageSpec
}

export interface VersionDto {
  id: string
  version: string
  sizeBytes: number
  sha256: string | null
  notes: string | null
  uploadedAt: string
}

export interface PackageDetailDto {
  id: string
  name: string
  description: string | null
  kind: PackageKind
  createdAt: string
  spec: PackageSpec
  versions: VersionDto[]
}

export type TargetKind = 'Device' | 'User' | 'Group'
export type VersionPolicy = 'Latest' | 'Pinned'

export type RolloutMode = 'Percent' | 'Groups'

/** Eine Stufe der Tranchenverteilung: je nach Modus mit Prozentwert oder Untergruppe. */
export interface RolloutStage {
  afterDays: number
  percent: number | null
  groupId: string | null
}

export interface Rollout {
  mode: RolloutMode
  stages: RolloutStage[]
}

export interface RolloutStatus {
  stagesReached: number
  stagesTotal: number
  releasedPercent: number | null
}

export type AssignmentIntent = 'Install' | 'Uninstall'

export interface AssignmentDto {
  id: string
  packageId: string
  targetKind: TargetKind
  target: string
  targetName: string | null
  versionPolicy: VersionPolicy
  pinnedVersionId: string | null
  createdAt: string
  rollout: Rollout | null
  rolloutStatus: RolloutStatus | null
  intent: AssignmentIntent
}

export interface ProfileDto {
  id: string
  name: string
  description: string | null
  itemCount: number
  createdAt: string
}

export interface ProfileItemDto {
  packageId: string
  packageName: string
  packageKind: PackageKind
  position: number
  versionPolicy: VersionPolicy
  pinnedVersionId: string | null
}

export interface ProfileDetailDto {
  id: string
  name: string
  description: string | null
  createdAt: string
  items: ProfileItemDto[]
}

export interface ProfileItemInput {
  packageId: string
  versionPolicy: VersionPolicy
  pinnedVersionId: string | null
}

export interface ProfileAssignmentDto {
  id: string
  profileId: string
  targetKind: TargetKind
  target: string
  targetName: string | null
  createdAt: string
  rollout: Rollout | null
  rolloutStatus: RolloutStatus | null
}

export interface SoftwareAssignmentInput {
  packageId: string
  targetKind: TargetKind
  target: string
  versionPolicy: VersionPolicy
  pinnedVersionId: string | null
  rollout: Rollout | null
  intent: AssignmentIntent
}

export interface CountDto {
  name: string
  count: number
}

export interface SilentDeviceDto {
  id: string
  name: string
  osName: string
  lastSeenAt: string | null
  registeredAt: string
}

export interface DeviceStatsDto {
  total: number
  online: number
  silent: number
  neverSeen: number
  byOs: CountDto[]
  byAgentVersion: CountDto[]
  silentDevices: SilentDeviceDto[]
}

export interface PackageStatsDto {
  packageId: string
  name: string
  installed: number
  inProgress: number
  failed: number
}

export interface FailureDto {
  installationId: string
  deviceId: string
  deviceName: string | null
  packageId: string
  packageName: string
  removal: boolean
  attempts: number
  message: string | null
  updatedAt: string
}

export interface SoftwareStatsDto {
  packages: number
  assignments: number
  installations: {
    installed: number
    pending: number
    running: number
    failed: number
    removing: number
    removalFailed: number
  }
  perPackage: PackageStatsDto[]
  failures: FailureDto[]
}

export interface InstallationDto {
  id: string
  deviceId: string
  deviceName: string | null
  packageId: string
  packageName: string
  targetVersion: string | null
  installedVersion: string | null
  status: 'Pending' | 'Running' | 'Installed' | 'Failed' | 'Removed'
  attempts: number
  message: string | null
  updatedAt: string
  removal: boolean
}

export type UpdateMode = 'SecurityOnly' | 'All'
export type RebootBehavior = 'Never' | 'IfRequired'
export type WeekDay = 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday' | 'Sunday'

export interface UpdatePolicyInput {
  name: string
  description: string | null
  mode: UpdateMode
  reboot: RebootBehavior
}

export interface UpdatePolicyDto extends UpdatePolicyInput {
  id: string
}

export interface MaintenanceWindowInput {
  name: string
  days: WeekDay[]
  startMinutes: number
  durationMinutes: number
  timeZoneId: string
}

export interface MaintenanceWindowDto extends MaintenanceWindowInput {
  id: string
}

export interface OsAssignmentDto {
  id: string
  scope: 'Policy' | 'Window'
  parentId: string
  targetKind: 'Device' | 'Group'
  target: string
  targetName: string | null
}

export interface UpdateRunDto {
  id: string
  deviceId: string
  deviceName: string | null
  windowName: string | null
  occurrenceStart: string
  status: 'Queued' | 'Succeeded' | 'Failed'
  installed: number | null
  failed: number | null
  rebootRequired: boolean
  rebooting: boolean
  message: string | null
  createdAt: string
  completedAt: string | null
}

export interface PagedResult<T> {
  items: T[]
  total: number
}

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    /** Erste Fehlermeldung des Servers (bei Validierungsfehlern), sonst undefined. */
    public readonly detail?: string,
  ) {
    super(`HTTP ${status}`)
  }
}

/** Liest die erste Meldung aus einer ASP.NET-ProblemDetails-Antwort (errors: { feld: [meldung] }). */
function firstProblemMessage(text: string): string | undefined {
  try {
    const body = JSON.parse(text) as { errors?: Record<string, string[]> }
    return body.errors ? Object.values(body.errors)[0]?.[0] : undefined
  } catch {
    return undefined
  }
}

let tokenProvider: () => string | null = () => null
let unauthorizedHandler: () => void = () => {}

export function configureApi(options: { token: () => string | null; onUnauthorized: () => void }) {
  tokenProvider = options.token
  unauthorizedHandler = options.onUnauthorized
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  headers.set('Accept', 'application/json')
  if (typeof init.body === 'string') headers.set('Content-Type', 'application/json')
  const token = tokenProvider()
  if (token) headers.set('Authorization', `Bearer ${token}`)

  const response = await fetch(`/api${path}`, { ...init, headers })

  if (response.status === 401 && token) unauthorizedHandler()
  if (!response.ok) throw new ApiError(response.status, firstProblemMessage(await response.text()))
  if (response.status === 204) return undefined as T

  return (await response.json()) as T
}

/** Upload mit Fortschrittsanzeige (fetch kann den Upload-Fortschritt nicht melden). */
export function uploadVersion(
  packageId: string,
  version: string,
  notes: string,
  file: File,
  onProgress: (fraction: number) => void,
): Promise<VersionDto> {
  return new Promise((resolve, reject) => {
    const form = new FormData()
    form.set('version', version)
    form.set('notes', notes)
    form.set('file', file)

    const xhr = new XMLHttpRequest()
    xhr.open('POST', `/api/software/packages/${packageId}/versions`)
    xhr.setRequestHeader('Accept', 'application/json')
    const token = tokenProvider()
    if (token) xhr.setRequestHeader('Authorization', `Bearer ${token}`)

    xhr.upload.onprogress = (e) => {
      if (e.lengthComputable) onProgress(e.loaded / e.total)
    }
    xhr.onload = () => {
      if (xhr.status === 401) unauthorizedHandler()
      if (xhr.status >= 200 && xhr.status < 300) resolve(JSON.parse(xhr.responseText) as VersionDto)
      else reject(new ApiError(xhr.status, firstProblemMessage(xhr.responseText)))
    }
    xhr.onerror = () => reject(new ApiError(0))
    xhr.send(form)
  })
}

export interface AgentPackageDto {
  platform: string
  version: string
  fileName: string
  size: number
  sha256: string
  url: string
}

export interface AgentPackagesDto {
  serverUrl: string
  packages: AgentPackageDto[]
}

export interface DeviceGroupRefDto {
  id: string
  name: string
  description: string | null
}

export const api = {
  device: (id: string) => request<DeviceDto>(`/devices/${id}`),
  deviceGroups: (id: string) => request<DeviceGroupRefDto[]>(`/devices/${id}/groups`),
  deviceInstallations: (id: string) => request<InstallationDto[]>(`/software/installations/by-device?deviceId=${id}`),
  agentPackages: () => request<AgentPackagesDto>('/agent/packages'),
  login: (userName: string, password: string) =>
    request<LoginResponse>('/auth/login', { method: 'POST', body: JSON.stringify({ userName, password }) }),
  me: () => request<UserDto>('/auth/me'),
  oidcProviders: () => request<OidcProviderDto[]>('/auth/oidc/providers'),
  /** Benutzer zu einem frisch erhaltenen Token (die Sitzung ist zu diesem Zeitpunkt noch nicht gesetzt). */
  meWithToken: async (token: string): Promise<UserDto> => {
    const response = await fetch('/api/auth/me', {
      headers: { Accept: 'application/json', Authorization: `Bearer ${token}` },
    })
    if (!response.ok) throw new ApiError(response.status)
    return (await response.json()) as UserDto
  },
  devices: (options: { search?: string; groupId?: string; skip?: number; take?: number } = {}) => {
    const query = new URLSearchParams()
    if (options.search) query.set('search', options.search)
    if (options.groupId) query.set('groupId', options.groupId)
    query.set('skip', String(options.skip ?? 0))
    query.set('take', String(options.take ?? 100))
    return request<PagedResult<DeviceDto>>(`/devices?${query}`)
  },
  updateAssignment: (id: string, input: AssignmentInput) =>
    request<DeviceDto>(`/devices/${id}/assignment`, { method: 'PUT', body: JSON.stringify(input) }),

  deviceStats: (silentHours: number) => request<DeviceStatsDto>(`/devices/stats?silentHours=${silentHours}`),
  softwareStats: () => request<SoftwareStatsDto>('/software/stats'),

  registrationTokens: () => request<TokenDto[]>('/agents/tokens'),
  createRegistrationToken: (label: string, validHours: number) =>
    request<TokenCreatedDto>('/agents/tokens', { method: 'POST', body: JSON.stringify({ label, validHours }) }),
  deleteRegistrationToken: (id: string) => request<void>(`/agents/tokens/${id}`, { method: 'DELETE' }),

  resetDeviceCertificate: (deviceId: string) =>
    request<void>(`/agents/devices/${deviceId}/certificate`, { method: 'DELETE' }),

  jobs: (deviceId: string) => request<JobDto[]>(`/agents/jobs?deviceId=${deviceId}`),
  createJob: (deviceId: string, type: string) =>
    request<JobDto>('/agents/jobs', { method: 'POST', body: JSON.stringify({ deviceId, type }) }),

  packages: () => request<PackageDto[]>('/software/packages'),
  package: (id: string) => request<PackageDetailDto>(`/software/packages/${id}`),
  createPackage: (name: string, description: string | null, kind: PackageKind, spec: PackageSpec) =>
    request<PackageDto>('/software/packages', {
      method: 'POST',
      body: JSON.stringify({ name, description, kind, spec }),
    }),
  updatePackage: (id: string, description: string | null, spec: PackageSpec, name?: string) =>
    request<void>(`/software/packages/${id}`, { method: 'PUT', body: JSON.stringify({ description, spec, name }) }),

  secrets: () => request<SecretDto[]>('/software/secrets'),
  setSecret: (name: string, value: string) =>
    request<void>(`/software/secrets/${encodeURIComponent(name)}`, { method: 'PUT', body: JSON.stringify({ value }) }),
  deleteSecret: (name: string) => request<void>(`/software/secrets/${encodeURIComponent(name)}`, { method: 'DELETE' }),
  deletePackage: (id: string) => request<void>(`/software/packages/${id}`, { method: 'DELETE' }),

  assignments: (packageId: string) => request<AssignmentDto[]>(`/software/assignments?packageId=${packageId}`),
  createAssignment: (input: SoftwareAssignmentInput) =>
    request<AssignmentDto>('/software/assignments', { method: 'POST', body: JSON.stringify(input) }),
  updatePackageAssignment: (
    id: string,
    versionPolicy: VersionPolicy,
    pinnedVersionId: string | null,
    rollout: Rollout | null,
    intent: AssignmentIntent,
  ) =>
    request<void>(`/software/assignments/${id}`, {
      method: 'PUT',
      body: JSON.stringify({ versionPolicy, pinnedVersionId, rollout, intent }),
    }),
  deleteAssignment: (id: string) => request<void>(`/software/assignments/${id}`, { method: 'DELETE' }),

  profiles: () => request<ProfileDto[]>('/software/profiles'),
  profile: (id: string) => request<ProfileDetailDto>(`/software/profiles/${id}`),
  createProfile: (name: string, description: string | null, items: ProfileItemInput[]) =>
    request<ProfileDetailDto>('/software/profiles', {
      method: 'POST',
      body: JSON.stringify({ name, description, items }),
    }),
  updateProfile: (id: string, description: string | null, items: ProfileItemInput[]) =>
    request<ProfileDetailDto>(`/software/profiles/${id}`, {
      method: 'PUT',
      body: JSON.stringify({ description, items }),
    }),
  deleteProfile: (id: string) => request<void>(`/software/profiles/${id}`, { method: 'DELETE' }),
  profileAssignments: (profileId: string) =>
    request<ProfileAssignmentDto[]>(`/software/profile-assignments?profileId=${profileId}`),
  createProfileAssignment: (profileId: string, targetKind: TargetKind, target: string, rollout: Rollout | null) =>
    request<ProfileAssignmentDto>('/software/profile-assignments', {
      method: 'POST',
      body: JSON.stringify({ profileId, targetKind, target, rollout }),
    }),
  deleteProfileAssignment: (id: string) => request<void>(`/software/profile-assignments/${id}`, { method: 'DELETE' }),

  installations: (packageId: string) => request<InstallationDto[]>(`/software/installations?packageId=${packageId}`),
  retryInstallation: (id: string) => request<void>(`/software/installations/${id}/retry`, { method: 'POST' }),

  osPolicies: () => request<UpdatePolicyDto[]>('/os-updates/policies'),
  createOsPolicy: (input: UpdatePolicyInput) =>
    request<UpdatePolicyDto>('/os-updates/policies', { method: 'POST', body: JSON.stringify(input) }),
  updateOsPolicy: (id: string, input: UpdatePolicyInput) =>
    request<UpdatePolicyDto>(`/os-updates/policies/${id}`, { method: 'PUT', body: JSON.stringify(input) }),
  deleteOsPolicy: (id: string) => request<void>(`/os-updates/policies/${id}`, { method: 'DELETE' }),

  osWindows: () => request<MaintenanceWindowDto[]>('/os-updates/windows'),
  createOsWindow: (input: MaintenanceWindowInput) =>
    request<MaintenanceWindowDto>('/os-updates/windows', { method: 'POST', body: JSON.stringify(input) }),
  updateOsWindow: (id: string, input: MaintenanceWindowInput) =>
    request<MaintenanceWindowDto>(`/os-updates/windows/${id}`, { method: 'PUT', body: JSON.stringify(input) }),
  deleteOsWindow: (id: string) => request<void>(`/os-updates/windows/${id}`, { method: 'DELETE' }),

  osAssignments: (scope: 'Policy' | 'Window', parentId: string) =>
    request<OsAssignmentDto[]>(`/os-updates/assignments?scope=${scope}&parentId=${parentId}`),
  createOsAssignment: (scope: 'Policy' | 'Window', parentId: string, targetKind: 'Device' | 'Group', target: string) =>
    request<OsAssignmentDto>('/os-updates/assignments', {
      method: 'POST',
      body: JSON.stringify({ scope, parentId, targetKind, target }),
    }),
  deleteOsAssignment: (id: string) => request<void>(`/os-updates/assignments/${id}`, { method: 'DELETE' }),

  osRuns: (options: { deviceId?: string; take?: number } = {}) => {
    const query = new URLSearchParams()
    if (options.deviceId) query.set('deviceId', options.deviceId)
    query.set('take', String(options.take ?? 100))
    return request<UpdateRunDto[]>(`/os-updates/runs?${query}`)
  },
  runOsUpdatesNow: (deviceId: string) => request<void>(`/os-updates/devices/${deviceId}/run`, { method: 'POST' }),

  groups: () => request<GroupDto[]>('/groups'),
  createGroup: (input: GroupInput) => request<GroupDto>('/groups', { method: 'POST', body: JSON.stringify(input) }),
  updateGroup: (id: string, input: GroupInput) =>
    request<GroupDto>(`/groups/${id}`, { method: 'PUT', body: JSON.stringify(input) }),
  deleteGroup: (id: string) => request<void>(`/groups/${id}`, { method: 'DELETE' }),
  previewGroup: (input: GroupInput) =>
    request<PagedResult<DeviceDto>>('/groups/preview', { method: 'POST', body: JSON.stringify(input) }),
}
