<script lang="ts">
  import { onMount } from 'svelte'
  import { api, type DeviceDto, type GroupDto } from './api'
  import { auth } from './auth.svelte'
  import DeviceJobs from './DeviceJobs.svelte'
  import DataTable, { type Column } from './DataTable.svelte'
  import { i18n } from './i18n.svelte'
  import { isOnline } from './status'

  let devices = $state<DeviceDto[]>([])
  let groups = $state<GroupDto[]>([])
  let total = $state(0)
  let loading = $state(true)
  let failed = $state(false)

  let groupId = $state('')

  // Editing the assignment
  let dialog: HTMLDialogElement
  let jobs: DeviceJobs
  let editing = $state<DeviceDto | null>(null)
  let form = $state({ primaryUser: '', orgUnit: '', costCenter: '' })
  let saving = $state(false)
  let saveFailed = $state(false)

  // Loads all devices (in blocks of 500); search, column filters and sorting happen in the table.
  async function load(group: string) {
    loading = true
    failed = false
    try {
      const all: DeviceDto[] = []
      let count = 0
      do {
        const result = await api.devices({ groupId: group, skip: all.length, take: 500 })
        count = result.total
        all.push(...result.items)
        if (result.items.length === 0) break
      } while (all.length < count && all.length < 10000)
      devices = all
      total = count
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

  $effect(() => {
    load(groupId)
  })

  const columns = $derived<Column<DeviceDto>[]>([
    { key: 'name', label: i18n.t('devices.name'), value: (d) => d.name, cell: nameCell, strong: true },
    {
      key: 'status',
      label: i18n.t('devices.status'),
      value: (d) => (isOnline(d) ? i18n.t('devices.online') : i18n.t('devices.offline')),
      filter: 'select',
      cell: status,
    },
    { key: 'os', label: i18n.t('devices.os'), value: (d) => `${d.osName} ${d.osVersion}`.trim(), cell: os },
    { key: 'user', label: i18n.t('devices.primaryUser'), value: (d) => d.primaryUser },
    { key: 'org', label: i18n.t('devices.orgUnit'), value: (d) => d.orgUnit, filter: 'select' },
    { key: 'cost', label: i18n.t('devices.costCenter'), value: (d) => d.costCenter, filter: 'select' },
    { key: 'agent', label: i18n.t('devices.agentVersion'), value: (d) => d.agentVersion, filter: 'select' },
    { key: 'seen', label: i18n.t('devices.lastSeen'), value: (d) => d.lastSeenAt ?? '', cell: seen },
  ])

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

{#snippet nameCell(device: DeviceDto)}
  <a href={`#/devices/${device.id}`}>{device.name}</a>
{/snippet}

{#snippet status(device: DeviceDto)}
  <span class="dot" class:on={isOnline(device)}></span>
  {isOnline(device) ? i18n.t('devices.online') : i18n.t('devices.offline')}
{/snippet}

{#snippet os(device: DeviceDto)}
  {device.osName} <span class="muted">{device.osVersion}</span>
{/snippet}

{#snippet seen(device: DeviceDto)}
  {device.lastSeenAt ? i18n.formatDate(device.lastSeenAt) : '–'}
{/snippet}

{#snippet rowActions(device: DeviceDto)}
  <button class="ghost" onclick={() => jobs.open(device)}>{i18n.t('jobs.open')}</button>
  <button class="ghost" onclick={() => openEdit(device)}>{i18n.t('common.edit')}</button>
{/snippet}

{#snippet groupFilter()}
  <select bind:value={groupId} aria-label={i18n.t('devices.group')}>
    <option value="">{i18n.t('devices.allGroups')}</option>
    {#each groups as group (group.id)}
      <option value={group.id}>{group.name} ({group.deviceCount})</option>
    {/each}
  </select>
{/snippet}

<section>
  <header class="page-head">
    <h1>{i18n.t('devices.title')}</h1>
    {#if !loading && !failed}
      <span class="muted">{i18n.t('devices.count', { count: total })}</span>
    {/if}
  </header>

  {#if failed}
    <div class="card">
      <p class="error">{i18n.t('devices.error')}</p>
      <button onclick={() => load(groupId)}>{i18n.t('devices.retry')}</button>
    </div>
  {:else}
    <DataTable
      rows={devices}
      {columns}
      rowKey={(d) => d.id}
      actions={auth.canEdit ? rowActions : undefined}
      searchPlaceholder={i18n.t('devices.search')}
      emptyText={i18n.t('devices.empty')}
      stale={loading}
      toolbar={groupFilter}
      defaultHidden={['org', 'cost']}
      storageKey="devices"
    />
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
    background: var(--ok);
    opacity: 1;
  }
</style>
