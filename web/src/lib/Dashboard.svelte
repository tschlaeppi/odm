<script lang="ts">
  import { onDestroy, onMount } from 'svelte'
  import { api, type DeviceStatsDto, type FailureDto, type PackageStatsDto, type SilentDeviceDto, type SoftwareStatsDto } from './api'
  import DataTable, { type Column } from './DataTable.svelte'
  import { i18n } from './i18n.svelte'

  let devices = $state<DeviceStatsDto | null>(null)
  let software = $state<SoftwareStatsDto | null>(null)
  let failed = $state(false)
  let silentHours = $state(24)
  let timer: ReturnType<typeof setInterval> | undefined

  async function load() {
    try {
      const [d, s] = await Promise.all([api.deviceStats(silentHours), api.softwareStats()])
      devices = d
      software = s
      failed = false
    } catch {
      failed = true
    }
  }

  onMount(() => {
    load()
    timer = setInterval(load, 30000)
  })
  onDestroy(() => clearInterval(timer))

  function agentLabel(version: string) {
    return version === '' ? i18n.t('dash.unknown') : version
  }

  const maxOs = $derived(Math.max(1, ...(devices?.byOs.map((c) => c.count) ?? [])))
  const maxAgent = $derived(Math.max(1, ...(devices?.byAgentVersion.map((c) => c.count) ?? [])))

  const silentColumns = $derived<Column<SilentDeviceDto>[]>([
    { key: 'name', label: i18n.t('devices.name'), value: (d) => d.name, cell: deviceLink, strong: true },
    { key: 'os', label: i18n.t('devices.os'), value: (d) => d.osName },
    { key: 'seen', label: i18n.t('devices.lastSeen'), value: (d) => d.lastSeenAt, cell: lastSeen, filter: 'none' },
    { key: 'registered', label: i18n.t('dev.detail.registered'), value: (d) => d.registeredAt, cell: registered, filter: 'none' },
  ])

  const failureColumns = $derived<Column<FailureDto>[]>([
    { key: 'device', label: i18n.t('devices.name'), value: (f) => f.deviceName ?? f.deviceId, cell: failureDevice, strong: true },
    { key: 'package', label: i18n.t('sw.name'), value: (f) => f.packageName, cell: failurePackage },
    { key: 'action', label: i18n.t('sw.intent'), value: (f) => i18n.t(f.removal ? 'sw.intent.Uninstall' : 'sw.intent.Install'), filter: 'select' },
    { key: 'attempts', label: i18n.t('sw.attempts'), value: (f) => f.attempts, align: 'right', filter: 'none' },
    { key: 'updated', label: i18n.t('dev.detail.updated'), value: (f) => f.updatedAt, cell: failureDate, filter: 'none' },
    { key: 'message', label: i18n.t('dev.detail.message'), value: (f) => f.message, cell: failureMessage, muted: true },
  ])

  function pct(part: number, total: number) {
    return total > 0 ? (part / total) * 100 : 0
  }
</script>

{#snippet deviceLink(d: SilentDeviceDto)}<a href={`#/devices/${d.id}`}>{d.name}</a>{/snippet}
{#snippet lastSeen(d: SilentDeviceDto)}{d.lastSeenAt ? i18n.formatDate(d.lastSeenAt) : i18n.t('dash.never')}{/snippet}
{#snippet registered(d: SilentDeviceDto)}{i18n.formatDate(d.registeredAt)}{/snippet}
{#snippet failureDevice(f: FailureDto)}<a href={`#/devices/${f.deviceId}`}>{f.deviceName ?? f.deviceId}</a>{/snippet}
{#snippet failurePackage(f: FailureDto)}<a href={`#/software/${f.packageId}`}>{f.packageName}</a>{/snippet}
{#snippet failureDate(f: FailureDto)}{i18n.formatDate(f.updatedAt)}{/snippet}
{#snippet failureMessage(f: FailureDto)}<span class="msg" title={f.message ?? ''}>{f.message ?? '–'}</span>{/snippet}

<section>
  <header class="page-head">
    <h1>{i18n.t('dash.title')}</h1>
    <span class="spacer"></span>
    <label class="inline">
      <span class="muted small">{i18n.t('dash.silentAfter')}</span>
      <select bind:value={silentHours} onchange={load}>
        <option value={1}>1 h</option>
        <option value={6}>6 h</option>
        <option value={24}>24 h</option>
        <option value={72}>3 d</option>
        <option value={168}>7 d</option>
      </select>
    </label>
  </header>

  {#if failed && !devices}
    <p class="error">{i18n.t('devices.error')}</p>
  {:else if !devices || !software}
    <p class="muted">{i18n.t('devices.loading')}</p>
  {:else}
    {@const sw = software.installations}
    {@const swTotal = sw.installed + sw.pending + sw.running + sw.failed + sw.removing + sw.removalFailed}
    <div class="kpis">
      <div class="card kpi"><span class="num">{devices.total}</span><span class="lbl">{i18n.t('dash.devices')}</span></div>
      <div class="card kpi"><span class="num ok">{devices.online}</span><span class="lbl">{i18n.t('devices.online')}</span></div>
      <div class="card kpi" class:alert={devices.silent > 0}>
        <span class="num" class:bad={devices.silent > 0}>{devices.silent}</span>
        <span class="lbl">{i18n.t('dash.silent')}</span>
      </div>
      <div class="card kpi" class:alert={sw.failed + sw.removalFailed > 0}>
        <span class="num" class:bad={sw.failed + sw.removalFailed > 0}>{sw.failed + sw.removalFailed}</span>
        <span class="lbl">{i18n.t('dash.failedInstalls')}</span>
      </div>
      <div class="card kpi"><span class="num">{sw.installed}</span><span class="lbl">{i18n.t('dash.installed')}</span></div>
      <div class="card kpi"><span class="num">{sw.pending + sw.running + sw.removing}</span><span class="lbl">{i18n.t('dash.inProgress')}</span></div>
    </div>

    <div class="grid">
      <div class="card">
        <h2>{i18n.t('dash.byOs')}</h2>
        {#each devices.byOs as c (c.name)}
          <div class="bar-row">
            <span class="bar-label" title={c.name}>{c.name || i18n.t('dash.unknown')}</span>
            <span class="bar"><span class="fill" style:width="{pct(c.count, maxOs)}%"></span></span>
            <span class="bar-count">{c.count}</span>
          </div>
        {:else}
          <p class="muted">–</p>
        {/each}
      </div>

      <div class="card">
        <h2>{i18n.t('dash.byAgent')}</h2>
        {#each devices.byAgentVersion as c (c.name)}
          <div class="bar-row">
            <span class="bar-label">{agentLabel(c.name)}</span>
            <span class="bar"><span class="fill" style:width="{pct(c.count, maxAgent)}%"></span></span>
            <span class="bar-count">{c.count}</span>
          </div>
        {:else}
          <p class="muted">–</p>
        {/each}
      </div>

      <div class="card">
        <h2>{i18n.t('dash.deployStatus')} ({swTotal})</h2>
        {#if swTotal === 0}
          <p class="muted">{i18n.t('sw.noInstallations')}</p>
        {:else}
          <div class="stack" role="img" aria-label={i18n.t('dash.deployStatus')}>
            <span class="seg ok" style:width="{pct(sw.installed, swTotal)}%"></span>
            <span class="seg run" style:width="{pct(sw.pending + sw.running + sw.removing, swTotal)}%"></span>
            <span class="seg err" style:width="{pct(sw.failed + sw.removalFailed, swTotal)}%"></span>
          </div>
          <ul class="legend">
            <li><span class="dot ok"></span>{i18n.t('sw.status.Installed')} {sw.installed}</li>
            <li><span class="dot run"></span>{i18n.t('dash.inProgress')} {sw.pending + sw.running + sw.removing}</li>
            <li><span class="dot err"></span>{i18n.t('sw.status.Failed')} {sw.failed + sw.removalFailed}</li>
          </ul>
        {/if}
      </div>
    </div>

    <h2>{i18n.t('dash.perPackage')}</h2>
    {#if software.perPackage.length === 0}
      <p class="muted">{i18n.t('sw.noInstallations')}</p>
    {:else}
      <div class="card">
        {#each software.perPackage as p (p.packageId)}
          {@render packageRow(p)}
        {/each}
      </div>
    {/if}

    <h2>{i18n.t('dash.recentFailures')} ({software.failures.length})</h2>
    <DataTable
      rows={software.failures}
      columns={failureColumns}
      rowKey={(f) => f.installationId}
      emptyText={i18n.t('dash.noFailures')}
      pageSize={10}
    />

    <h2>{i18n.t('dash.silentDevices')} ({devices.silent})</h2>
    <DataTable
      rows={devices.silentDevices}
      columns={silentColumns}
      rowKey={(d) => d.id}
      emptyText={i18n.t('dash.noSilent')}
      pageSize={10}
    />
  {/if}
</section>

{#snippet packageRow(p: PackageStatsDto)}
  {@const total = p.installed + p.inProgress + p.failed}
  <div class="bar-row package">
    <a class="bar-label" href={`#/software/${p.packageId}`} title={p.name}>{p.name}</a>
    <span class="stack slim">
      <span class="seg ok" style:width="{pct(p.installed, total)}%"></span>
      <span class="seg run" style:width="{pct(p.inProgress, total)}%"></span>
      <span class="seg err" style:width="{pct(p.failed, total)}%"></span>
    </span>
    <span class="bar-count">{p.installed}/{total}{p.failed > 0 ? ` · ${p.failed} ✕` : ''}</span>
  </div>
{/snippet}

<style>
  h2 {
    font-size: 1rem;
    margin: 1.5rem 0 0.5rem;
  }
  .card h2 {
    margin-top: 0;
  }
  .inline {
    display: flex;
    align-items: center;
    gap: 0.5rem;
  }
  .kpis {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(10rem, 1fr));
    gap: 0.75rem;
  }
  .kpi {
    display: grid;
    gap: 0.15rem;
  }
  .kpi.alert {
    border-color: var(--danger);
  }
  .num {
    font-size: 1.9rem;
    font-weight: 700;
    line-height: 1.1;
  }
  .num.ok {
    color: var(--ok);
  }
  .num.bad {
    color: var(--danger);
  }
  .lbl {
    font-size: 0.8rem;
    color: var(--muted);
  }
  .grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(18rem, 1fr));
    gap: 0.75rem;
    margin-top: 0.75rem;
  }
  .bar-row {
    display: grid;
    grid-template-columns: minmax(6rem, 10rem) 1fr 3rem;
    align-items: center;
    gap: 0.6rem;
    padding: 0.2rem 0;
    font-size: 0.85rem;
  }
  .bar-row.package {
    grid-template-columns: minmax(8rem, 14rem) 1fr minmax(4rem, 8rem);
  }
  .bar-label {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .bar-count {
    text-align: right;
    color: var(--muted);
    font-variant-numeric: tabular-nums;
  }
  .bar,
  .stack {
    display: flex;
    height: 0.7rem;
    background: var(--hover);
    border-radius: 0.2rem;
    overflow: hidden;
  }
  .stack:not(.slim) {
    height: 1.1rem;
  }
  .fill {
    background: var(--accent);
  }
  .seg.ok,
  .dot.ok {
    background: var(--ok);
  }
  .seg.run,
  .dot.run {
    background: var(--accent);
  }
  .seg.err,
  .dot.err {
    background: var(--danger);
  }
  .legend {
    display: flex;
    flex-wrap: wrap;
    gap: 0.4rem 1rem;
    list-style: none;
    margin: 0.75rem 0 0;
    padding: 0;
    font-size: 0.85rem;
  }
  .dot {
    display: inline-block;
    width: 0.6rem;
    height: 0.6rem;
    border-radius: 50%;
    margin-right: 0.4rem;
  }
  .msg {
    display: inline-block;
    max-width: 24rem;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    vertical-align: bottom;
  }
  .error {
    color: var(--danger);
  }
</style>
