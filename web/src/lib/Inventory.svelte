<script lang="ts">
  import { onMount } from 'svelte'
  import { api, downloadSoftwareCsv, type SoftwareRowDto, type SoftwareSummaryDto } from './api'
  import DataTable, { type Column } from './DataTable.svelte'
  import { i18n } from './i18n.svelte'

  let query = $state('')
  let summary = $state<SoftwareSummaryDto[]>([])
  let rows = $state<SoftwareRowDto[]>([])
  let selected = $state<SoftwareSummaryDto | null>(null)
  let loading = $state(false)
  let failed = $state(false)

  async function search() {
    const name = query.trim()
    selected = null
    rows = []
    if (!name) {
      summary = []
      return
    }
    loading = true
    try {
      summary = await api.softwareSummary(name)
      failed = false
    } catch {
      failed = true
    } finally {
      loading = false
    }
  }

  async function select(item: SoftwareSummaryDto) {
    selected = item
    try {
      rows = await api.softwareRows(item.name, item.version)
      failed = false
    } catch {
      failed = true
    }
  }

  async function exportCsv() {
    try {
      await downloadSoftwareCsv(selected?.name ?? query.trim(), selected?.version ?? null)
    } catch {
      failed = true
    }
  }

  function submit(event: SubmitEvent) {
    event.preventDefault()
    search()
  }

  onMount(() => {})

  const summaryColumns = $derived<Column<SoftwareSummaryDto>[]>([
    { key: 'name', label: i18n.t('inv.name'), value: (s) => s.name, strong: true },
    { key: 'version', label: i18n.t('inv.version'), value: (s) => s.version },
    { key: 'devices', label: i18n.t('inv.devices'), value: (s) => s.devices, align: 'right', filter: 'none' },
  ])

  const rowColumns = $derived<Column<SoftwareRowDto>[]>([
    { key: 'device', label: i18n.t('inv.device'), value: (r) => r.deviceName, cell: deviceCell, strong: true },
    { key: 'name', label: i18n.t('inv.name'), value: (r) => r.name },
    { key: 'version', label: i18n.t('inv.version'), value: (r) => r.version },
    { key: 'publisher', label: i18n.t('inv.publisher'), value: (r) => r.publisher, muted: true },
  ])
</script>

{#snippet deviceCell(r: SoftwareRowDto)}
  <a href={`#/devices/${r.deviceId}`}>{r.deviceName}</a>
{/snippet}

{#snippet pick(s: SoftwareSummaryDto)}
  <button class="ghost" onclick={() => select(s)}>{i18n.t('inv.showDevices')}</button>
{/snippet}

<section>
  <h1>{i18n.t('inv.title')}</h1>
  <p class="muted intro">{i18n.t('inv.intro')}</p>

  <form class="card filters" onsubmit={submit}>
    <label class="grow">
      <span>{i18n.t('inv.name')}</span>
      <input bind:value={query} maxlength="200" />
    </label>
    <button type="submit" disabled={loading}>{i18n.t('audit.search')}</button>
    <button type="button" class="ghost" disabled={!query.trim()} onclick={exportCsv}>{i18n.t('inv.csv')}</button>
  </form>

  {#if failed}
    <p class="error">{i18n.t('devices.error')}</p>
  {:else if query.trim()}
    <DataTable
      rows={summary}
      columns={summaryColumns}
      rowKey={(s) => `${s.name}\u0000${s.version ?? ''}`}
      actions={pick}
      stale={loading}
      emptyText={i18n.t('inv.empty')}
    />
    {#if selected}
      <h2>{selected.name} {selected.version ?? ''}</h2>
      <DataTable rows={rows} columns={rowColumns} rowKey={(r) => `${r.deviceId}\u0000${r.name}\u0000${r.version ?? ''}`} emptyText={i18n.t('inv.empty')} pageSize={25} />
    {/if}
  {/if}
</section>

<style>
  h1 {
    font-size: 1.35rem;
    margin: 0 0 0.5rem;
  }
  h2 {
    font-size: 1rem;
    margin: 1.5rem 0 0.5rem;
  }
  .intro {
    margin: 0 0 1rem;
    max-width: 46rem;
  }
  .filters {
    display: flex;
    flex-wrap: wrap;
    gap: 0.75rem;
    align-items: flex-end;
    margin-bottom: 1rem;
  }
  .filters label {
    display: flex;
    flex-direction: column;
    gap: 0.25rem;
    font-size: 0.85rem;
  }
  .grow {
    flex: 1;
    min-width: 12rem;
  }
</style>
