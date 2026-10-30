import type { DeviceDto } from './api'

// Heartbeat-Intervall ist 60 s; nach drei verpassten Meldungen gilt ein Gerät als offline.
const ONLINE_WINDOW_MS = 3 * 60_000

export function isOnline(device: DeviceDto): boolean {
  return device.lastSeenAt !== null && Date.now() - Date.parse(device.lastSeenAt) < ONLINE_WINDOW_MS
}
