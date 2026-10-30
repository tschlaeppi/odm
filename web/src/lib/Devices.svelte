<script lang="ts">
  import { onMount } from 'svelte'
  import { api, type DeviceDto, type GroupDto } from './api'
  import { auth } from './auth.svelte'
  import DeviceJobs from './DeviceJobs.svelte'
  import { i18n } from './i18n.svelte'
  import { isOnline } from './status'

  let devices = $state<DeviceDto[]>([])
  let groups = $state<GroupDto[]>([])
  let total = $state(0)
  let loading = $state(true)
  let failed = $state(false)

  let search = $state('')
  let groupId = $state('')

  // Bearbeiten der Zuordnung
  let dialog: HTMLDialogElement
  let jobs: DeviceJobs
  let editing = $state<DeviceDto | null>(null)
  let form = $state({ primaryUser: '', orgUnit: '', costCenter: '' })
  let saving = $state(false)
  let saveFailed = $state(false)

  async function load(searchTerm: string, group: string) {
    loading = true
    failed = false
    try {
      const result = await api.devices({ search: searchTerm.trim(), groupId: group })
      devices = result.items
      total = result.total
    } catch {
      failed = true
    } finally {
      loading = false
    }
  }

  onMount(async () => {
    try {
      groups = await api.groups()
    } catch {
      groups = []
    }
  })

  // Filter ändern -> (leicht verzögert) neu laden
  $effect(() => {
    const s = search
    const g = groupId
    const timer = setTimeout(() => load(s, g), 250)
    return () => clearTimeout(timer)
  })

  function openEdit(device: DeviceDto) {
    editing = device
    form = {
      primaryUser: device.primaryUser ?? '',
      orgUnit: device.orgUnit ?? '',
      costCenter: device.costCenter ?? '',
    }
    saveFailed = false
    dialog.showModal()
  }

  async function save(event: SubmitEvent) {
    event.preventDefault()
    if (!editing) return
    saving = true
    saveFailed = false
    try {
      const updated = await api.updateAssignment(editing.id, {
        primaryUser: form.primaryUser.trim() || null,
        orgUnit: form.orgUnit.trim() || null,
        costCenter: form.costCenter.trim() || null,
      })
      devices = devices.map((d) => (d.id === updated.id ? updated : d))
      dialog.close()
    } catch {
      saveFailed = true
    } finally {
      saving = false
    }
  }
</script>

<section>
  <header class="head">
    <h1>{i18n.t('devices.title')}</h1>
    {#if !loading && !failed}
      <span class="muted">{i18n.t('devices.count', { count: total })}</span>
    {/if}
  </header>

  <div class="toolbar">
    <input type="search" placeholder={i18n.t('devices.search')} bind:value={search} />
    <select bind:value={groupId} aria-label={i18n.t('devices.group')}>
      <option value="">{i18n.t('devices.allGroups')}</option>
      {#each groups as group (group.id)}
        <option value={group.id}>{group.name} ({group.deviceCount})</option>
      {/each}
    </select>
  </div>

  {#if failed}
    <div class="card">
      <p class="error">{i18n.t('devices.error')}</p>
      <button onclick={() => load(search, groupId)}>{i18n.t('devices.retry')}</button>
    </div>
  {:else if loading && devices.length === 0}
    <p class="muted">{i18n.t('devices.loading')}</p>
  {:else if devices.length === 0}
    <p class="muted">{search || groupId ? i18n.t('devices.noMatch') : i18n.t('devices.empty')}</p>
  {:else}
    <div class="table-wrap card" class:stale={loading}>
      <table>
        <thead>
          <tr>
            <th>{i18n.t('devices.name')}</th>
            <th>{i18n.t('devices.status')}</th>
            <th>{i18n.t('devices.os')}</th>
            <th>{i18n.t('devices.primaryUser')}</th>
            <th>{i18n.t('devices.orgUnit')}</th>
            <th>{i18n.t('devices.costCenter')}</th>
            <th>{i18n.t('devices.lastSeen')}</th>
            {#if auth.canEdit}<th></th>{/if}
          </tr>
        </thead>
        <tbody>
          {#each devices as device (device.id)}
            <tr>
              <td class="strong">{device.name}</td>
              <td>
                <span class="dot" class:on={isOnline(device)}></span>
                {isOnline(device) ? i18n.t('devices.online') : i18n.t('devices.offline')}
              </td>
              <td>{device.osName} <span class="muted">{device.osVersion}</span></td>
              <td>{device.primaryUser ?? '–'}</td>
              <td>{device.orgUnit ?? '–'}</td>
              <td>{device.costCenter ?? '–'}</td>
              <td>{i18n.formatDate(device.lastSeenAt)}</td>
              {#if auth.canEdit}
                <td class="actions">
                  <button class="ghost" onclick={() => jobs.open(device)}>{i18n.t('jobs.open')}</button>
                  <button class="ghost" onclick={() => openEdit(device)}>{i18n.t('common.edit')}</button>
                </td>
              {/if}
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  {/if}
</section>

<DeviceJobs bind:this={jobs} />

<dialog bind:this={dialog} onclose={() => (editing = null)}>
  <form onsubmit={save} class="dialog-form">
    <h2>{i18n.t('devices.editAssignment')}</h2>
    <p class="muted">{editing?.name}</p>

    <label>
      <span>{i18n.t('devices.primaryUser')}</span>
      <input bind:value={form.primaryUser} maxlength="256" />
    </label>
    <label>
      <span>{i18n.t('devices.orgUnit')}</span>
      <input bind:value={form.orgUnit} maxlength="256" />
    </label>
    <label>
      <span>{i18n.t('devices.costCenter')}</span>
      <input bind:value={form.costCenter} maxlength="64" />
    </label>

    {#if saveFailed}
      <p class="error" role="alert">{i18n.t('common.saveFailed')}</p>
    {/if}

    <div class="buttons">
      <button type="button" onclick={() => dialog.close()}>{i18n.t('common.cancel')}</button>
      <button class="primary" type="submit" disabled={saving}>{i18n.t('common.save')}</button>
    </div>
  </form>
</dialog>

<style>
  .head {
    display: flex;
    align-items: baseline;
    gap: 1rem;
    margin-bottom: 1rem;
  }
  h1 {
    font-size: 1.35rem;
    margin: 0;
  }
  .table-wrap {
    padding: 0;
    overflow-x: auto;
  }
  .table-wrap.stale {
    opacity: 0.6;
  }
  table {
    width: 100%;
    border-collapse: collapse;
    font-size: 0.92rem;
  }
  th,
  td {
    text-align: left;
    padding: 0.7rem 1rem;
    white-space: nowrap;
  }
  th {
    font-weight: 500;
    font-size: 0.8rem;
    color: var(--muted);
    border-bottom: 1px solid var(--border);
  }
  tbody tr + tr td {
    border-top: 1px solid var(--border);
  }
  tbody tr:hover {
    background: var(--hover);
  }
  .strong {
    font-weight: 600;
  }
  .actions {
    text-align: right;
  }
  .error {
    color: var(--danger);
    margin-top: 0;
  }
  .dot {
    display: inline-block;
    width: 0.55rem;
    height: 0.55rem;
    border-radius: 50%;
    margin-right: 0.4rem;
    background: var(--muted);
    opacity: 0.5;
  }
  .dot.on {
    background: #22a06b;
    opacity: 1;
  }
</style>
