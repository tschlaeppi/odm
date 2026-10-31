<script lang="ts">
  import { onDestroy, onMount } from 'svelte'
  import {
    api,
    ApiError,
    type MaintenanceWindowDto,
    type OsAssignmentDto,
    type RebootBehavior,
    type UpdateMode,
    type UpdatePolicyDto,
    type UpdateRunDto,
    type WeekDay,
  } from './api'
  import { auth } from './auth.svelte'
  import DataTable, { type Column } from './DataTable.svelte'
  import { i18n, type MessageKey } from './i18n.svelte'
  import TargetPicker from './TargetPicker.svelte'

  const weekDays: WeekDay[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']
  const durations = [30, 60, 120, 180, 240, 360, 480, 720, 1440]
  const statusClass = { Queued: 'run', Succeeded: 'ok', Failed: 'err' } as const

  let policies = $state<UpdatePolicyDto[]>([])
  let windows = $state<MaintenanceWindowDto[]>([])
  let runs = $state<UpdateRunDto[]>([])
  // Assignments per policy or window
  let assignments = $state<Record<string, OsAssignmentDto[]>>({})
  let loading = $state(true)
  let errorKey = $state<MessageKey | null>(null)
  let timer: ReturnType<typeof setInterval> | undefined

  // Policy form
  let policyName = $state('')
  let policyMode = $state<UpdateMode>('SecurityOnly')
  let policyReboot = $state<RebootBehavior>('Never')

  // Window form (also for editing)
  const localZone = Intl.DateTimeFormat().resolvedOptions().timeZone
  const zones: string[] = (Intl as unknown as { supportedValuesOf?: (key: string) => string[] }).supportedValuesOf?.('timeZone') ?? [localZone]
  let editingWindowId = $state<string | null>(null)
  let windowName = $state('')
  let windowDays = $state<WeekDay[]>(['Saturday'])
  let windowStart = $state('02:00')
  let windowDuration = $state(120)
  let windowZone = $state(localZone)

  async function loadAssignments() {
    const next: Record<string, OsAssignmentDto[]> = {}
    await Promise.all([
      ...policies.map(async (p) => (next[p.id] = await api.osAssignments('Policy', p.id))),
      ...windows.map(async (w) => (next[w.id] = await api.osAssignments('Window', w.id))),
    ])
    assignments = next
  }

  async function load() {
    try {
      ;[policies, windows] = await Promise.all([api.osPolicies(), api.osWindows()])
      await loadAssignments()
      runs = await api.osRuns()
    } catch {
      errorKey = 'devices.error'
    } finally {
      loading = false
    }
  }

  async function refreshRuns() {
    try {
      runs = await api.osRuns()
    } catch {
      /* next attempt at the next interval */
    }
  }

  onMount(() => {
    load()
    timer = setInterval(refreshRuns, 15000)
  })
  onDestroy(() => clearInterval(timer))

  function conflictOr(e: unknown, conflictKey: MessageKey): MessageKey {
    return e instanceof ApiError && e.status === 409 ? conflictKey : 'common.saveFailed'
  }

  async function createPolicy(event: SubmitEvent) {
    event.preventDefault()
    errorKey = null
    try {
      await api.createOsPolicy({ name: policyName.trim(), description: null, mode: policyMode, reboot: policyReboot })
      policyName = ''
      await load()
    } catch (e) {
      errorKey = conflictOr(e, 'os.nameTaken')
    }
  }

  async function changePolicy(policy: UpdatePolicyDto, mode: UpdateMode, reboot: RebootBehavior) {
    try {
      await api.updateOsPolicy(policy.id, { name: policy.name, description: policy.description, mode, reboot })
    } finally {
      await load()
    }
  }

  async function removePolicy(policy: UpdatePolicyDto) {
    if (!confirm(i18n.t('os.confirmDelete', { name: policy.name }))) return
    await api.deleteOsPolicy(policy.id)
    await load()
  }

  function toMinutes(time: string): number {
    const [h, m] = time.split(':').map(Number)
    return h * 60 + m
  }

  function toTime(minutes: number): string {
    return `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`
  }

  function resetWindowForm() {
    editingWindowId = null
    windowName = ''
    windowDays = ['Saturday']
    windowStart = '02:00'
    windowDuration = 120
    windowZone = localZone
  }

  function editWindow(w: MaintenanceWindowDto) {
    editingWindowId = w.id
    windowName = w.name
    windowDays = [...w.days]
    windowStart = toTime(w.startMinutes)
    windowDuration = w.durationMinutes
    windowZone = w.timeZoneId
  }

  async function saveWindow(event: SubmitEvent) {
    event.preventDefault()
    errorKey = null
    const input = {
      name: windowName.trim(),
      days: windowDays,
      startMinutes: toMinutes(windowStart),
      durationMinutes: windowDuration,
      timeZoneId: windowZone,
    }
    try {
      if (editingWindowId) await api.updateOsWindow(editingWindowId, input)
      else await api.createOsWindow(input)
      resetWindowForm()
      await load()
    } catch (e) {
      errorKey = conflictOr(e, 'os.nameTaken')
    }
  }

  async function removeWindow(w: MaintenanceWindowDto) {
    if (!confirm(i18n.t('os.confirmDelete', { name: w.name }))) return
    await api.deleteOsWindow(w.id)
    if (editingWindowId === w.id) resetWindowForm()
    await load()
  }

  function toggleDay(day: WeekDay) {
    windowDays = windowDays.includes(day) ? windowDays.filter((d) => d !== day) : [...windowDays, day]
  }

  async function addTarget(scope: 'Policy' | 'Window', parentId: string, kind: 'Device' | 'Group', target: string) {
    errorKey = null
    try {
      await api.createOsAssignment(scope, parentId, kind, target)
      assignments[parentId] = await api.osAssignments(scope, parentId)
    } catch (e) {
      errorKey = conflictOr(e, 'os.targetTaken')
    }
  }

  async function removeTarget(scope: 'Policy' | 'Window', parentId: string, id: string) {
    await api.deleteOsAssignment(id)
    assignments[parentId] = await api.osAssignments(scope, parentId)
  }

  function runSummary(r: UpdateRunDto): string {
    const parts: string[] = []
    if (r.installed !== null) parts.push(`${i18n.t('os.installed')}: ${r.installed}`)
    if (r.failed) parts.push(`${i18n.t('os.failedCount')}: ${r.failed}`)
    if (r.rebooting) parts.push(i18n.t('os.rebooting'))
    else if (r.rebootRequired) parts.push(i18n.t('os.rebootRequired'))
    return parts.join(' · ')
  }

  function durationText(minutes: number): string {
    return minutes % 60 === 0 ? i18n.t('os.hours', { count: minutes / 60 }) : i18n.t('os.minutes', { count: minutes })
  }

  function daysText(w: MaintenanceWindowDto): string {
    return weekDays.filter((d) => w.days.includes(d)).map((d) => i18n.t(`day.${d}` as MessageKey)).join(', ')
  }

  const policyColumns = $derived<Column<UpdatePolicyDto>[]>([
    { key: 'name', label: i18n.t('os.name'), value: (p) => p.name, strong: true },
    { key: 'mode', label: i18n.t('os.mode'), value: (p) => i18n.t(`os.mode.${p.mode}` as MessageKey), cell: policyMode_, filter: 'select' },
    { key: 'reboot', label: i18n.t('os.reboot'), value: (p) => i18n.t(`os.reboot.${p.reboot}` as MessageKey), cell: policyReboot_, filter: 'select' },
    { key: 'targets', label: i18n.t('os.targets'), value: (p) => (assignments[p.id] ?? []).map((a) => a.targetName ?? a.target).join(' '), cell: policyTargets },
  ])

  const windowColumns = $derived<Column<MaintenanceWindowDto>[]>([
    { key: 'name', label: i18n.t('os.name'), value: (w) => w.name, strong: true },
    { key: 'days', label: i18n.t('os.days'), value: (w) => daysText(w) },
    { key: 'start', label: i18n.t('os.start'), value: (w) => toTime(w.startMinutes), filter: 'none' },
    { key: 'duration', label: i18n.t('os.duration'), value: (w) => durationText(w.durationMinutes), filter: 'select' },
    { key: 'zone', label: i18n.t('os.timeZone'), value: (w) => w.timeZoneId, filter: 'select' },
    { key: 'targets', label: i18n.t('os.targets'), value: (w) => (assignments[w.id] ?? []).map((a) => a.targetName ?? a.target).join(' '), cell: windowTargets },
  ])

  const runColumns = $derived<Column<UpdateRunDto>[]>([
    { key: 'when', label: i18n.t('os.when'), value: (r) => r.createdAt, cell: runWhen, filter: 'none' },
    { key: 'device', label: i18n.t('devices.name'), value: (r) => r.deviceName ?? r.deviceId, cell: runDevice, strong: true },
    { key: 'window', label: i18n.t('os.window'), value: (r) => r.windowName ?? i18n.t('os.manual'), filter: 'select' },
    { key: 'status', label: i18n.t('sw.status'), value: (r) => i18n.t(`os.status.${r.status}` as MessageKey), cell: runStatus, filter: 'select' },
    { key: 'result', label: i18n.t('dev.detail.message'), value: (r) => runSummary(r), cell: runResult, muted: true },
  ])
</script>

{#snippet policyMode_(p: UpdatePolicyDto)}
  <select value={p.mode} disabled={!auth.canEdit} onchange={(e) => changePolicy(p, e.currentTarget.value as UpdateMode, p.reboot)}>
    <option value="SecurityOnly">{i18n.t('os.mode.SecurityOnly')}</option>
    <option value="All">{i18n.t('os.mode.All')}</option>
  </select>
{/snippet}

{#snippet policyReboot_(p: UpdatePolicyDto)}
  <select value={p.reboot} disabled={!auth.canEdit} onchange={(e) => changePolicy(p, p.mode, e.currentTarget.value as RebootBehavior)}>
    <option value="Never">{i18n.t('os.reboot.Never')}</option>
    <option value="IfRequired">{i18n.t('os.reboot.IfRequired')}</option>
  </select>
{/snippet}

{#snippet policyTargets(p: UpdatePolicyDto)}{@render targets('Policy', p.id)}{/snippet}
{#snippet windowTargets(w: MaintenanceWindowDto)}{@render targets('Window', w.id)}{/snippet}

{#snippet policyActions(p: UpdatePolicyDto)}
  {#if auth.canEdit}
    <button class="ghost" onclick={() => removePolicy(p)}>{i18n.t('common.delete')}</button>
  {/if}
{/snippet}

{#snippet windowActions(w: MaintenanceWindowDto)}
  {#if auth.canEdit}
    <button class="ghost" onclick={() => editWindow(w)}>{i18n.t('common.edit')}</button>
    <button class="ghost" onclick={() => removeWindow(w)}>{i18n.t('common.delete')}</button>
  {/if}
{/snippet}

{#snippet runWhen(r: UpdateRunDto)}{i18n.formatDate(r.createdAt)}{/snippet}
{#snippet runDevice(r: UpdateRunDto)}<a href={`#/devices/${r.deviceId}`}>{r.deviceName ?? r.deviceId}</a>{/snippet}
{#snippet runStatus(r: UpdateRunDto)}
  <span class="badge {statusClass[r.status]}">{i18n.t(`os.status.${r.status}` as MessageKey)}</span>
{/snippet}
{#snippet runResult(r: UpdateRunDto)}
  <span class="msg" title={i18n.server(r.message)}>{runSummary(r) || '–'}</span>
{/snippet}

<section>
  <h1>{i18n.t('os.title')}</h1>
  <p class="muted intro">{i18n.t('os.intro')}</p>

  {#if errorKey}<p class="error" role="alert">{i18n.t(errorKey)}</p>{/if}

  {#if loading}
    <p class="muted">{i18n.t('devices.loading')}</p>
  {:else}
    <!-- Policies -->
    <h2>{i18n.t('os.policies')}</h2>

    {#if auth.canEdit}
      <form class="card form" onsubmit={createPolicy}>
        <div class="row">
          <label class="grow">
            <span>{i18n.t('os.name')}</span>
            <input bind:value={policyName} required maxlength="128" />
          </label>
          <label>
            <span>{i18n.t('os.mode')}</span>
            <select bind:value={policyMode}>
              <option value="SecurityOnly">{i18n.t('os.mode.SecurityOnly')}</option>
              <option value="All">{i18n.t('os.mode.All')}</option>
            </select>
          </label>
          <label>
            <span>{i18n.t('os.reboot')}</span>
            <select bind:value={policyReboot}>
              <option value="Never">{i18n.t('os.reboot.Never')}</option>
              <option value="IfRequired">{i18n.t('os.reboot.IfRequired')}</option>
            </select>
          </label>
          <button class="primary" type="submit">{i18n.t('os.newPolicy')}</button>
        </div>
        <p class="muted small">{i18n.t('os.policyPerTarget')}</p>
      </form>
    {/if}

    <DataTable
      rows={policies}
      columns={policyColumns}
      rowKey={(p) => p.id}
      actions={policyActions}
      emptyText={i18n.t('os.noPolicies')}
      pageSize={25}
    />

    <!-- Maintenance windows -->
    <h2>{i18n.t('os.windows')}</h2>

    {#if auth.canEdit}
      <form class="card form" onsubmit={saveWindow}>
        <div class="row">
          <label class="grow">
            <span>{i18n.t('os.name')}</span>
            <input bind:value={windowName} required maxlength="128" />
          </label>
          <label>
            <span>{i18n.t('os.start')}</span>
            <input type="time" bind:value={windowStart} required />
          </label>
          <label>
            <span>{i18n.t('os.duration')}</span>
            <select bind:value={windowDuration}>
              {#each durations as d}
                <option value={d}>
                  {d % 60 === 0 ? i18n.t('os.hours', { count: d / 60 }) : i18n.t('os.minutes', { count: d })}
                </option>
              {/each}
            </select>
          </label>
          <label>
            <span>{i18n.t('os.timeZone')}</span>
            <select bind:value={windowZone}>
              {#each zones as zone}
                <option value={zone}>{zone}</option>
              {/each}
            </select>
          </label>
        </div>
        <div class="row days">
          <span class="muted small">{i18n.t('os.days')}:</span>
          {#each weekDays as day}
            <label class="check">
              <input type="checkbox" checked={windowDays.includes(day)} onchange={() => toggleDay(day)} />
              {i18n.t(`day.${day}` as MessageKey)}
            </label>
          {/each}
        </div>
        <div class="row">
          <button class="primary" type="submit" disabled={windowDays.length === 0}>
            {editingWindowId ? i18n.t('os.saveWindow') : i18n.t('os.newWindow')}
          </button>
          {#if editingWindowId}
            <button type="button" onclick={resetWindowForm}>{i18n.t('common.cancel')}</button>
          {/if}
        </div>
        <p class="muted small">{i18n.t('os.windowRule')}</p>
      </form>
    {/if}

    <DataTable
      rows={windows}
      columns={windowColumns}
      rowKey={(w) => w.id}
      actions={windowActions}
      emptyText={i18n.t('os.noWindows')}
      pageSize={25}
    />

    <!-- Runs -->
    <h2>{i18n.t('os.runs')}</h2>
    {#if runs.length === 0}
      <p class="muted">{i18n.t('os.noRuns')}</p>
    {:else}
      <DataTable rows={runs} columns={runColumns} rowKey={(r) => r.id} pageSize={25} />
    {/if}
  {/if}
</section>

{#snippet targets(scope: 'Policy' | 'Window', parentId: string)}
  <div class="targets">
    <span class="muted small">{i18n.t('os.targets')}:</span>
    {#if (assignments[parentId] ?? []).length === 0}
      <span class="muted small">{i18n.t('os.noTargets')}</span>
    {/if}
    {#each assignments[parentId] ?? [] as a (a.id)}
      <span class="chip">
        {i18n.t(`target.kind.${a.targetKind}` as MessageKey)}: {a.targetName ?? a.target}
        {#if auth.canEdit}
          <button class="x" onclick={() => removeTarget(scope, parentId, a.id)} aria-label={i18n.t('os.removeTarget')}>✕</button>
        {/if}
      </span>
    {/each}
  </div>
  {#if auth.canEdit}
    <TargetPicker label={i18n.t('os.addTarget')} onpick={(kind, target) => addTarget(scope, parentId, kind, target)} />
  {/if}
{/snippet}

<style>
  h1 {
    font-size: 1.35rem;
    margin: 0 0 0.5rem;
  }
  h2 {
    font-size: 1.05rem;
    margin: 1.75rem 0 0.75rem;
  }
  h3 {
    font-size: 1rem;
    margin: 0;
  }
  .intro {
    margin: 0 0 1rem;
    max-width: 48rem;
  }
  .form,
  .item {
    display: grid;
    gap: 0.75rem;
    margin-bottom: 0.75rem;
  }
  .item header {
    display: flex;
    justify-content: space-between;
    align-items: start;
  }
  .row {
    display: flex;
    flex-wrap: wrap;
    gap: 0.75rem;
    align-items: end;
  }
  .days {
    align-items: center;
  }
  .grow {
    flex: 1;
    min-width: 12rem;
  }
  label {
    display: grid;
    gap: 0.35rem;
    font-size: 0.85rem;
    color: var(--muted);
  }
  label.check {
    display: flex;
    align-items: center;
    gap: 0.3rem;
    color: var(--text);
  }
  .small {
    font-size: 0.85rem;
    margin: 0;
  }
  .targets {
    display: flex;
    flex-wrap: wrap;
    gap: 0.4rem;
    align-items: center;
  }
  .chip {
    display: inline-flex;
    align-items: center;
    gap: 0.3rem;
    padding: 0.15rem 0.6rem;
    border: 1px solid var(--border);
    border-radius: 999px;
    font-size: 0.85rem;
  }
  .x {
    border: none;
    background: transparent;
    padding: 0 0.2rem;
    color: var(--muted);
  }
  .list {
    padding: 0;
    overflow-x: auto;
  }
  .strong {
    font-weight: 600;
  }
  .error {
    color: var(--danger);
    margin: 0 0 0.75rem;
  }
  .msg {
    display: inline-block;
    max-width: 28rem;
    overflow: hidden;
    text-overflow: ellipsis;
    vertical-align: bottom;
  }
</style>
