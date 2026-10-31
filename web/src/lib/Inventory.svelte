<script lang="ts">
  import { onMount } from 'svelte'
  import {
    api,
    downloadCsv,
    downloadSoftwareCsv,
    type ComplianceDto,
    type MissingSoftwareDto,
    type SoftwareRowDto,
    type SoftwareSummaryDto,
  } from './api'
  import DataTable, { type Column } from './DataTable.svelte'
  import { i18n, type MessageKey } from './i18n.svelte'

  const reasons = ['Failed', 'Pending', 'Running', 'VersionMismatch', 'RemovalPending', 'RemovalFailed']

  let tab = $state<'programs' | 'missing' | 'compliance'>('programs')
  let missing = $state<MissingSoftwareDto[]>([])
  let compliance = $state<ComplianceDto | null>(null)
  let reason = $state('')
  let query = $state('')
  let summary = $state<SoftwareSummaryDto[]>([])
  let rows = $state<SoftwareRowDto[]>([])
  let selected = $state<SoftwareSummaryDto | null>(null)
  let loading = $state(false)
  let failed = $state(false)

  onMount(search)

  async function search() {
    const name = query.trim()
    selected = null
    rows = []
    if (!name && tab === 'missing') {
      missing = []
      return
    }
    loading = true
    try {
      if (tab === 'missing') missing = await api.softwareMissing(name)
      else summary = await api.softwareSummary(name)
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

  async function loadCompliance() {
    loading = true
    try {
      compliance = await api.compliance(reason)
      failed = false
    } catch {
      failed = true
    } finally {
      loading = false
    }
  }

  function show(next: typeof tab) {
    tab = next
    failed = false
    if (next === 'compliance') loadCompliance()
  }

  async function exportCsv() {
    try {
      if (tab === 'compliance') await downloadCsv('/software/compliance.csv', { reason }, 'compliance.csv')
      else if (tab === 'missing') await downloadCsv('/inventory/software/missing.csv', { name: query.trim() }, 'devices-without-software.csv')
      else await downloadSoftwareCsv(selected?.name ?? query.trim(), selected?.version ?? null)
    } catch {
      failed = true
    }
  }

  function submit(event: SubmitEvent) {
    event.preventDefault()
    search()
  }


  const summaryColumns = $derived<Column<SoftwareSummaryDto>[]>([
    { key: 'name', label: i18n.t('inv.name'), value: (s) => s.name, strong: true },
    { key: 'version', label: i18n.t('inv.version'), value: (s) => s.version },
    { key: 'devices', label: i18n.t('inv.devices'), value: (s) => s.devices, align: 'right', filter: 'none' },
  ])

  const missingColumns = $derived<Column<MissingSoftwareDto>[]>([
    { key: 'device', label: i18n.t('inv.device'), value: (r) => r.deviceName, cell: missingDevice, strong: true },
    { key: 'os', label: i18n.t('devices.os'), value: (r) => r.osName },
    { key: 'collected', label: i18n.t('inv.collected'), value: (r) => r.collectedAt, cell: missingCollected, filter: 'none' },
  ])

  const complianceColumns = $derived<Column<ComplianceDtoRow>[]>([
    { key: 'device', label: i18n.t('inv.device'), value: (r) => r.deviceName, cell: complianceDevice, strong: true },
    { key: 'package', label: i18n.t('sw.name'), value: (r) => r.packageName },
    { key: 'reason', label: i18n.t('cmp.reason'), value: (r) => reasonLabel(r.reason), filter: 'select' },
    { key: 'target', label: i18n.t('dev.detail.target'), value: (r) => r.targetVersion },
    { key: 'installed', label: i18n.t('dev.detail.installed'), value: (r) => r.installedVersion },
    { key: 'updated', label: i18n.t('dev.detail.updated'), value: (r) => r.updatedAt, cell: complianceUpdated, filter: 'none' },
  ])

  type ComplianceDtoRow = ComplianceDto['rows'][number]

  function reasonLabel(value: string) {
    return i18n.t(`cmp.reason.${value}` as MessageKey)
  }

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

{#snippet missingDevice(r: MissingSoftwareDto)}
  <a href={`#/devices/${r.deviceId}`}>{r.deviceName}</a>
{/snippet}

{#snippet missingCollected(r: MissingSoftwareDto)}
  {i18n.formatDate(r.collectedAt)}
{/snippet}

{#snippet complianceDevice(r: ComplianceDtoRow)}
  <a href={`#/devices/${r.deviceId}`}>{r.deviceName}</a>
{/snippet}

{#snippet complianceUpdated(r: ComplianceDtoRow)}
  {i18n.formatDate(r.updatedAt)}
{/snippet}

{#snippet pick(s: SoftwareSummaryDto)}
  <button class="ghost" onclick={() => select(s)}>{i18n.t('inv.showDevices')}</button>
{/snippet}

<section>
  <h1>{i18n.t('inv.title')}</h1>

  <div class="tabs">
    <button class="ghost" class:active={tab === 'programs'} onclick={() => show('programs')}>{i18n.t('inv.tab.programs')}</button>
    <button class="ghost" class:active={tab === 'missing'} onclick={() => show('missing')}>{i18n.t('inv.tab.missing')}</button>
    <button class="ghost" class:active={tab === 'compliance'} onclick={() => show('compliance')}>{i18n.t('inv.tab.compliance')}</button>
  </div>

  {#if tab === 'compliance'}
    <p class="muted intro">{i18n.t('cmp.intro')}</p>
    <form class="card filters" onsubmit={(e) => { e.preventDefault(); loadCompliance() }}>
      <label>
        <span>{i18n.t('cmp.reason')}</span>
        <select bind:value={reason} onchange={loadCompliance}>
          <option value="">{i18n.t('audit.all')}</option>
          {#each reasons as r}
            <option value={r}>{reasonLabel(r)}{compliance?.byReason[r] ? ` (${compliance.byReason[r]})` : ''}</option>
          {/each}
        </select>
      </label>
      <button type="button" class="ghost" onclick={exportCsv}>{i18n.t('inv.csv')}</button>
    </form>
    {#if failed}
      <p class="error">{i18n.t('devices.error')}</p>
    {:else if compliance}
      <p class="muted">{i18n.t('cmp.summary', { deviating: compliance.deviating, total: compliance.total })}</p>
      <DataTable
        rows={compliance.rows}
        columns={complianceColumns}
        rowKey={(r) => `${r.deviceId}\u0000${r.packageId}`}
        stale={loading}
        emptyText={i18n.t('cmp.empty')}
        pageSize={25}
      />
    {/if}
  {:else}
    <p class="muted intro">{tab === 'missing' ? i18n.t('inv.missingIntro') : i18n.t('inv.intro')}</p>
    <form class="card filters" onsubmit={submit}>
      <label class="grow">
        <span>{i18n.t('inv.name')}</span>
        <input bind:value={query} maxlength="200" />
      </label>
      <button type="submit" disabled={loading}>{i18n.t('audit.search')}</button>
      <button type="button" class="ghost" disabled={tab === 'missing' && !query.trim()} onclick={exportCsv}>{i18n.t('inv.csv')}</button>
    </form>

    {#if failed}
      <p class="error">{i18n.t('devices.error')}</p>
    {:else if tab === 'missing'}
      {#if query.trim()}
      <DataTable rows={missing} columns={missingColumns} rowKey={(r) => r.deviceId} stale={loading} emptyText={i18n.t('inv.missingEmpty')} pageSize={25} />
      {/if}
    {:else}
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
  .tabs {
    display: flex;
    gap: 0.25rem;
    margin-bottom: 1rem;
  }
  .tabs .active {
    font-weight: 600;
    border-color: currentColor;
  }
  .grow {
    flex: 1;
    min-width: 12rem;
  }
</style>
