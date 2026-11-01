<script lang="ts">
  import { onMount } from 'svelte'
  import { api, type UpdateHistoryItemDto } from './api'
  import DataTable, { type Column } from './DataTable.svelte'
  import { i18n, type MessageKey } from './i18n.svelte'

  // Without deviceId the history of all devices is shown.
  let { deviceId = undefined }: { deviceId?: string } = $props()

  const pageLimit = 500
  let items = $state<UpdateHistoryItemDto[]>([])
  let total = $state(0)
  let query = $state('')
  let loading = $state(true)
  let failed = $state(false)
  let timer: ReturnType<typeof setTimeout> | undefined

  async function load() {
    loading = true
    try {
      const page = await api.osHistory({ deviceId, q: query.trim() || undefined, take: pageLimit })
      items = page.items
      total = page.total
      failed = false
    } catch {
      failed = true
    } finally {
      loading = false
    }
  }

  function onInput() {
    clearTimeout(timer)
    timer = setTimeout(load, 300)
  }

  onMount(() => {
    load()
    return () => clearTimeout(timer)
  })

  const statusClass = { Queued: 'run', Succeeded: 'ok', Failed: 'err' } as const

  const columns = $derived<Column<UpdateHistoryItemDto>[]>([
    { key: 'at', label: i18n.t('os.when'), value: (i) => i.at, cell: when, filter: 'none' },
    ...(deviceId ? [] : [{ key: 'device', label: i18n.t('devices.name'), value: (i: UpdateHistoryItemDto) => i.deviceName ?? i.deviceId, cell: device, strong: true } as Column<UpdateHistoryItemDto>]),
    { key: 'title', label: i18n.t('os.update'), value: (i) => i.title },
    { key: 'status', label: i18n.t('sw.status'), value: (i) => i18n.t(`os.status.${i.status}` as MessageKey), cell: status, filter: 'select' },
  ])
</script>

{#snippet when(i: UpdateHistoryItemDto)}{i18n.formatDate(i.at)}{/snippet}
{#snippet device(i: UpdateHistoryItemDto)}<a href={`#/devices/${i.deviceId}`}>{i.deviceName ?? i.deviceId}</a>{/snippet}
{#snippet status(i: UpdateHistoryItemDto)}
  <span class="badge {statusClass[i.status]}">{i18n.t(`os.status.${i.status}` as MessageKey)}</span>
{/snippet}

<div class="search">
  <input type="search" bind:value={query} oninput={onInput} placeholder={i18n.t('os.history.search')} aria-label={i18n.t('os.history.search')} />
</div>

{#if failed}
  <p class="error">{i18n.t('devices.error')}</p>
{:else}
  {#if total > items.length}
    <p class="muted">{i18n.t('audit.showing')} {items.length} {i18n.t('audit.of')} {total}</p>
  {/if}
  <DataTable
    rows={items}
    {columns}
    rowKey={(i) => `${i.runId}\u0000${i.title}`}
    stale={loading}
    emptyText={i18n.t('os.history.empty')}
    pageSize={25}
  />
{/if}

<style>
  .search {
    margin-bottom: 0.75rem;
  }
  .search input {
    width: 100%;
    max-width: 24rem;
  }
</style>
