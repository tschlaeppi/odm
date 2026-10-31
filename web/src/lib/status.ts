import type { DeviceDto } from './api'

// Heartbeat interval is 60 s; after three missed reports a device counts as offline.
const ONLINE_WINDOW_MS = 3 * 60_000

export function isOnline(device: DeviceDto): boolean {
  return device.lastSeenAt !== null && Date.now() - Date.parse(device.lastSeenAt) < ONLINE_WINDOW_MS
}
