<script lang="ts">
  import { onMount } from 'svelte'
  import { api, type UpdateRunDto } from './api'
  import DataTable, { type Column } from './DataTable.svelte'
  import { i18n, type MessageKey } from './i18n.svelte'

  let { deviceId }: { deviceId: string } = $props()

  let runs = $state<UpdateRunDto[]>([])
  let failed = $state(false)

  async function load() {
    try {
      runs = await api.osRuns({ deviceId })
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

  const statusClass = { Queued: 'run', Succeeded: 'ok', Failed: 'err' } as const

  function summary(r: UpdateRunDto): string {
    const parts: string[] = []
    if (r.installed !== null) parts.push(`${i18n.t('os.installed')}: ${r.installed}`)
    if (r.failed) parts.push(`${i18n.t('os.failedCount')}: ${r.failed}`)
    if (r.rebooting) parts.push(i18n.t('os.rebooting'))
    else if (r.rebootRequired) parts.push(i18n.t('os.rebootRequired'))
    return parts.join(' · ')
  }

  const columns = $derived<Column<UpdateRunDto>[]>([
    { key: 'when', label: i18n.t('os.when'), value: (r) => r.createdAt, cell: when, filter: 'none' },
    { key: 'window', label: i18n.t('os.window'), value: (r) => r.windowName ?? i18n.t('os.manual'), filter: 'select' },
    { key: 'status', label: i18n.t('sw.status'), value: (r) => i18n.t(`os.status.${r.status}` as MessageKey), cell: status, filter: 'select' },
    { key: 'result', label: i18n.t('dev.detail.message'), value: (r) => summary(r), cell: result, muted: true },
  ])
</script>

{#snippet when(r: UpdateRunDto)}{i18n.formatDate(r.createdAt)}{/snippet}
{#snippet status(r: UpdateRunDto)}
  <span class="badge {statusClass[r.status]}">{i18n.t(`os.status.${r.status}` as MessageKey)}</span>
{/snippet}
{#snippet result(r: UpdateRunDto)}
  <span class="msg" title={i18n.server(r.message)}>{summary(r) || '–'}</span>
{/snippet}

{#if failed}
  <p class="error">{i18n.t('devices.error')}</p>
{:else}
  <DataTable rows={runs} {columns} rowKey={(r) => r.id} emptyText={i18n.t('os.noRuns')} pageSize={10} />
{/if}

<style>
  .msg {
    display: inline-block;
    max-width: 28rem;
    overflow: hidden;
    text-overflow: ellipsis;
    vertical-align: bottom;
  }
</style>
