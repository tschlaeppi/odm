<script lang="ts">
  import { onMount } from 'svelte'
  import { api, type DeviceProfileDto, type DeviceProfileItemDto } from './api'
  import DataTable, { type Column } from './DataTable.svelte'
  import { i18n, type MessageKey } from './i18n.svelte'

  let { deviceId }: { deviceId: string } = $props()

  let profiles = $state<DeviceProfileDto[]>([])
  let failed = $state(false)

  async function load() {
    try {
      profiles = await api.deviceProfiles(deviceId)
      failed = false
    } catch {
      failed = true
    }
  }

  onMount(() => {
    load()
    const timer = setInterval(load, 15000)
    return () => clearInterval(timer)
  })

  const statusClass: Record<string, string> = {
    Pending: '',
    Running: 'run',
    Installed: 'ok',
    Failed: 'err',
    Removed: '',
    Scheduled: '',
    NotApplicable: '',
    NoVersion: 'err',
  }

  const columns = $derived<Column<DeviceProfileItemDto>[]>([
    { key: 'position', label: '#', value: (i) => i.position + 1, align: 'right', filter: 'none' },
    { key: 'package', label: i18n.t('sw.name'), value: (i) => i.packageName, cell: packageName, strong: true },
    { key: 'status', label: i18n.t('sw.status'), value: (i) => i18n.t(`sw.status.${i.status}` as MessageKey), cell: status, filter: 'select' },
    { key: 'target', label: i18n.t('dev.detail.target'), value: (i) => i.targetVersion },
    { key: 'installed', label: i18n.t('dev.detail.installed'), value: (i) => i.installedVersion },
    { key: 'message', label: i18n.t('dev.detail.message'), value: (i) => i.message, cell: message, muted: true },
  ])

  function source(p: DeviceProfileDto): string {
    const kind = i18n.t(`dev.profiles.source.${p.source}` as MessageKey)
    return p.sourceName ? `${kind}: ${p.sourceName}` : kind
  }
</script>

{#snippet packageName(i: DeviceProfileItemDto)}
  <a href={`#/software/${i.packageId}`}>{i.packageName}</a>
{/snippet}

{#snippet status(i: DeviceProfileItemDto)}
  <span class="badge {statusClass[i.status] ?? ''}">{i18n.t(`sw.status.${i.status}` as MessageKey)}</span>
{/snippet}

{#snippet message(i: DeviceProfileItemDto)}
  <span class="msg" title={i18n.server(i.message)}>{i18n.server(i.message) || '–'}</span>
{/snippet}

{#if failed}
  <p class="error">{i18n.t('devices.error')}</p>
{:else if profiles.length > 0}
  <h2>{i18n.t('dev.profiles')} ({profiles.length})</h2>
  {#each profiles as p (p.profileId + p.source + (p.sourceName ?? ''))}
    <h3>
      <a href={`#/profiles/${p.profileId}`}>{p.profileName}</a>
      <span class="muted small">· {source(p)}</span>
    </h3>
    <DataTable rows={p.items} {columns} rowKey={(i) => i.packageId} pageSize={25} />
  {/each}
{/if}

<style>
  h3 {
    font-size: 1rem;
    margin: 1rem 0 0.5rem;
  }
  .msg {
    display: inline-block;
    max-width: 28rem;
    overflow: hidden;
    text-overflow: ellipsis;
    vertical-align: bottom;
  }
</style>
